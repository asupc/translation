#!/usr/bin/env python3
"""把 ECDICT CSV 构建为应用使用的 SQLite 词典库。

用法:
    python scripts/build-dictionary.py --csv models/dictionary/ecdict.csv \
        --out models/dictionary/ecdict.sqlite

只依赖标准库。入库规则:
- 只保留 translation 非空的词条（本功能只展示中文释义，definition 英文释义不入库）;
- word 保留原始大小写（China 与 china 是不同词条，主键不能合并）;
- 由词条 exchange 的 p/d/i/3/s/r/t 七类词形生成"变形词 -> 原词"索引;
- exchange 含 "0:原词" 的词条若未被正向索引覆盖，按原词 exchange 反查类型兜底补入。
"""

import argparse
import csv
import os
import sqlite3
import sys
import time

# exchange 中参与变形索引的七类词形
FORM_KINDS = ("p", "d", "i", "3", "s", "r", "t")

CREATE_ENTRIES = """
CREATE TABLE entries (
    word        TEXT PRIMARY KEY,
    phonetic    TEXT,
    translation TEXT,
    pos         TEXT,
    collins     INTEGER NOT NULL DEFAULT 0,
    oxford      INTEGER NOT NULL DEFAULT 0,
    tag         TEXT,
    bnc         INTEGER NOT NULL DEFAULT -1,
    frq         INTEGER NOT NULL DEFAULT -1,
    exchange    TEXT
)
"""

CREATE_INFLECTIONS = """
CREATE TABLE inflections (
    form  TEXT PRIMARY KEY,
    lemma TEXT NOT NULL,
    kinds TEXT NOT NULL
)
"""

CREATE_NOCASE_INDEX = """
CREATE INDEX IF NOT EXISTS idx_entries_word_nocase ON entries(word COLLATE NOCASE)
"""


def parse_int(value, default):
    try:
        return int(value)
    except (TypeError, ValueError):
        return default


def clean(value):
    value = (value or "").strip()
    return value or None


def parse_forms(exchange):
    """把 'p:did/d:done' 拆成 {kind: form}；只保留 FORM_KINDS 中的键。"""
    forms = {}
    if not exchange:
        return forms
    for part in exchange.split("/"):
        kind, separator, form = part.strip().partition(":")
        kind = kind.strip()
        form = form.strip()
        if separator and kind in FORM_KINDS and form and kind not in forms:
            forms[kind] = form
    return forms


def parse_lemma(exchange):
    """取 exchange 中 '0:原词' 的原词；没有则返回 None。"""
    if not exchange:
        return None
    for part in exchange.split("/"):
        kind, separator, form = part.strip().partition(":")
        if separator and kind == "0":
            return form.strip() or None
    return None


def load_rows(csv_path):
    """读 CSV，返回（按词频排序的入库行, 原词词形表, 0:原词链接表）。"""
    rows = []
    with open(csv_path, "r", encoding="utf-8-sig", newline="") as handle:
        reader = csv.DictReader(handle)
        for record in reader:
            word = (record.get("word") or "").strip()
            translation = (record.get("translation") or "").strip()
            if not word or not translation:
                continue

            rows.append((
                word,
                clean(record.get("phonetic")),
                translation,
                clean(record.get("pos")),
                parse_int(record.get("collins"), 0),
                parse_int(record.get("oxford"), 0),
                clean(record.get("tag")),
                parse_int(record.get("bnc"), -1),
                parse_int(record.get("frq"), -1),
                (record.get("exchange") or "").strip() or None,
            ))

    if not rows:
        sys.exit("CSV 中没有可用词条（translation 全为空?）")

    # 常用词（frq 排名小）在前。后续 forms_by_word 按此顺序构建，
    # 变形索引冲突时自然保留更常用原词；INSERT OR IGNORE 也保留靠前行。
    rows.sort(key=lambda row: row[8] if row[8] >= 0 else 1 << 30)

    forms_by_word = {}
    zero_links = []
    for row in rows:
        word, exchange = row[0], row[9]
        forms = parse_forms(exchange)
        if forms:
            forms_by_word.setdefault(word, forms)
        lemma = parse_lemma(exchange)
        if lemma:
            zero_links.append((word, lemma))

    return rows, forms_by_word, zero_links


def build_inflections(forms_by_word, zero_links):
    """生成 {form小写: (lemma, kinds逗号分隔)}；正向索引优先，0: 链接兜底。"""
    order = {kind: index for index, kind in enumerate(FORM_KINDS)}
    index = {}
    for lemma, forms in forms_by_word.items():
        kinds_by_form = {}
        for kind, form in forms.items():
            key = form.lower()
            if key == lemma.lower():
                continue  # 词形与原词相同（如过去分词=原形），不需要索引
            kinds_by_form.setdefault(key, []).append(kind)
        for key, kinds in kinds_by_form.items():
            if key not in index:
                kinds.sort(key=lambda kind: order.get(kind, len(order)))
                index[key] = (lemma, ",".join(kinds))

    for word, lemma in zero_links:
        key = word.lower()
        if key in index or key == lemma.lower() or lemma not in forms_by_word:
            continue
        forms = forms_by_word[lemma]
        kinds = [kind for kind, form in forms.items() if form.lower() == key]
        kinds.sort(key=lambda kind: order.get(kind, len(order)))
        index[key] = (lemma, ",".join(kinds))

    return index


def remove_with_retry(path, attempts=5):
    """Windows 上 Explorer/杀毒软件可能短暂占用文件。"""
    for attempt in range(attempts):
        try:
            if os.path.exists(path):
                os.remove(path)
            return
        except PermissionError:
            if attempt == attempts - 1:
                raise
            time.sleep(0.2)


def main():
    parser = argparse.ArgumentParser(description="构建 ECDICT SQLite 词典库")
    parser.add_argument("--csv", required=True, help="ECDICT CSV 路径")
    parser.add_argument("--out", required=True, help="输出 SQLite 路径")
    args = parser.parse_args()

    if not os.path.isfile(args.csv):
        sys.exit(f"CSV 不存在: {args.csv}")

    rows, forms_by_word, zero_links = load_rows(args.csv)
    inflections = build_inflections(forms_by_word, zero_links)

    out_directory = os.path.dirname(os.path.abspath(args.out))
    os.makedirs(out_directory, exist_ok=True)
    temporary = args.out + ".tmp"
    remove_with_retry(temporary)

    try:
        connection = sqlite3.connect(temporary)
        try:
            connection.execute("PRAGMA journal_mode = OFF")
            connection.execute(CREATE_ENTRIES)
            connection.execute(CREATE_INFLECTIONS)
            with connection:
                connection.executemany(
                    "INSERT OR IGNORE INTO entries (word, phonetic, translation, pos, "
                    "collins, oxford, tag, bnc, frq, exchange) "
                    "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    rows)
                connection.executemany(
                    "INSERT OR IGNORE INTO inflections (form, lemma, kinds) VALUES (?, ?, ?)",
                    [(form, lemma, kinds) for form, (lemma, kinds) in inflections.items()])
            connection.execute(CREATE_NOCASE_INDEX)
            connection.execute("ANALYZE")
            connection.commit()
        finally:
            connection.close()

        # 先写临时文件再原子替换，构建失败不会破坏已有词典库。
        os.replace(temporary, args.out)
    except Exception:
        remove_with_retry(temporary)
        raise

    size_mb = os.path.getsize(args.out) / (1024 * 1024)
    print(f"词条 {len(rows)} 条，变形索引 {len(inflections)} 条")
    print(f"已生成 {args.out} ({size_mb:.1f} MB)")


if __name__ == "__main__":
    main()

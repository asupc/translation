#pragma once

#ifdef __cplusplus
extern "C" {
#endif

#if defined(_WIN32)
#  if defined(TRANSLATION_CORE_EXPORTS)
#    define TRANSLATION_CORE_API __declspec(dllexport)
#  else
#    define TRANSLATION_CORE_API __declspec(dllimport)
#  endif
#else
#  define TRANSLATION_CORE_API
#endif

typedef struct TranslationEngine TranslationEngine;

TRANSLATION_CORE_API TranslationEngine* translation_engine_create(
    const char* model_dir,
    int num_threads,
    char* error_message,
    int error_message_size);

TRANSLATION_CORE_API char* translation_engine_translate(
    TranslationEngine* engine,
    const char* text,
    int beam_size,
    int max_decoding_length,
    char* error_message,
    int error_message_size);

TRANSLATION_CORE_API void translation_engine_free_string(char* text);

TRANSLATION_CORE_API void translation_engine_destroy(TranslationEngine* engine);

TRANSLATION_CORE_API const char* translation_engine_version(void);

#ifdef __cplusplus
}
#endif

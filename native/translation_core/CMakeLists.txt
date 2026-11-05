cmake_minimum_required(VERSION 3.20)
project(translation_core VERSION 0.1.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

set(SPM_ENABLE_SHARED OFF CACHE BOOL "" FORCE)
set(SPM_ENABLE_TCMALLOC OFF CACHE BOOL "" FORCE)
set(SPM_BUILD_TEST OFF CACHE BOOL "" FORCE)
add_subdirectory(${CMAKE_CURRENT_LIST_DIR}/../third_party/sentencepiece
                 sentencepiece EXCLUDE_FROM_ALL)

# 共享库链接静态 SentencePiece 时必须带 -fPIC（Linux/macOS）。
set_target_properties(sentencepiece-static PROPERTIES POSITION_INDEPENDENT_CODE ON)

add_library(translation_core SHARED translation_core.cpp)
target_include_directories(translation_core PRIVATE
  ${CMAKE_CURRENT_LIST_DIR}
  ${CMAKE_CURRENT_LIST_DIR}/../third_party/CTranslate2/include
  ${CMAKE_CURRENT_LIST_DIR}/../third_party/sentencepiece
  ${CMAKE_CURRENT_LIST_DIR}/../third_party/sentencepiece/src
)
target_compile_definitions(translation_core PRIVATE
  TRANSLATION_CORE_EXPORTS
  TRANSLATION_CORE_VERSION="${PROJECT_VERSION}"
)
# 源码含中文注释；MSVC 需要 /utf-8 才能稳定按 UTF-8 解析（不依赖系统代码页）。
target_compile_options(translation_core PRIVATE
  $<$<CXX_COMPILER_ID:MSVC>:/utf-8>
)

set(CT2_BUILD_DIR ${CMAKE_CURRENT_LIST_DIR}/../build)
if(WIN32)
  set(CT2_LIBRARY ${CT2_BUILD_DIR}/ctranslate2.lib)
  if(NOT EXISTS ${CT2_LIBRARY})
    message(FATAL_ERROR
      "找不到 ${CT2_LIBRARY}；先运行 scripts/build-native.ps1 构建 CTranslate2。")
  endif()
else()
  find_library(CT2_LIBRARY NAMES ctranslate2
    PATHS ${CT2_BUILD_DIR} ${CT2_BUILD_DIR}/lib NO_DEFAULT_PATH)
  if(NOT CT2_LIBRARY)
    message(FATAL_ERROR
      "找不到 CTranslate2 库；先按 docs/BUILD.md 构建并安装到 native/build。")
  endif()
endif()

target_link_libraries(translation_core PRIVATE
  ${CT2_LIBRARY}
  sentencepiece-static
)

if(WIN32)
  add_custom_command(TARGET translation_core POST_BUILD
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
      ${CT2_BUILD_DIR}/ctranslate2.dll
      $<TARGET_FILE_DIR:translation_core>
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
      ${CT2_BUILD_DIR}/dnnl.dll
      $<TARGET_FILE_DIR:translation_core>
    COMMAND ${CMAKE_COMMAND} -E copy_if_different
      $<TARGET_FILE:translation_core>
      ${CT2_BUILD_DIR}/translation_core.dll
  )
endif()

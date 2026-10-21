cmake_minimum_required(VERSION 3.20)
project(translation_core LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

set(SPM_ENABLE_SHARED OFF CACHE BOOL "" FORCE)
set(SPM_ENABLE_TCMALLOC OFF CACHE BOOL "" FORCE)
set(SPM_BUILD_TEST OFF CACHE BOOL "" FORCE)
add_subdirectory(${CMAKE_CURRENT_LIST_DIR}/../third_party/sentencepiece
                 sentencepiece EXCLUDE_FROM_ALL)

add_library(translation_core SHARED translation_core.cpp)
target_include_directories(translation_core PRIVATE
  ${CMAKE_CURRENT_LIST_DIR}
  ${CMAKE_CURRENT_LIST_DIR}/../third_party/CTranslate2/include
  ${CMAKE_CURRENT_LIST_DIR}/../third_party/sentencepiece
  ${CMAKE_CURRENT_LIST_DIR}/../third_party/sentencepiece/src
)
target_compile_definitions(translation_core PRIVATE TRANSLATION_CORE_EXPORTS)
target_link_libraries(translation_core PRIVATE
  ${CMAKE_CURRENT_LIST_DIR}/../build/ctranslate2.lib
  sentencepiece-static
)
add_custom_command(TARGET translation_core POST_BUILD
  COMMAND ${CMAKE_COMMAND} -E copy_if_different
    ${CMAKE_CURRENT_LIST_DIR}/../build/ctranslate2.dll
    $<TARGET_FILE_DIR:translation_core>
  COMMAND ${CMAKE_COMMAND} -E copy_if_different
    ${CMAKE_CURRENT_LIST_DIR}/../build/dnnl.dll
    $<TARGET_FILE_DIR:translation_core>
  COMMAND ${CMAKE_COMMAND} -E copy_if_different
    $<TARGET_FILE:translation_core>
    ${CMAKE_CURRENT_LIST_DIR}/../build/translation_core.dll
)

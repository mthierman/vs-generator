#include <print>

// https://learn.microsoft.com/en-us/cpp/c-language/using-wmain
auto wmain([[maybe_unused]] int argc, [[maybe_unused]] wchar_t* argv[], [[maybe_unused]] wchar_t* envp[]) -> int {
    std::println();

    return 0;
}

[private]
just:
    just -l

# Normalize C# whitespace using the repo-local .editorconfig as the source of truth.
[group('refactoring')]
format:
    # Unity keeps no committed solution, so the script folders are formatted directly.
    dotnet format whitespace Assets/Scripts --folder
    dotnet format whitespace Assets/Tests --folder

alias f := format

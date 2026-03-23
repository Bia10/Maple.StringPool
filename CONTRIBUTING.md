# Contributing to Maple.StringPool

Thank you for considering contributing!

## How to Contribute

1. Fork the repository and create a feature branch from `main`.
2. Run `dotnet tool restore` to install local tools (CSharpier).
3. Ensure your code passes all checks:
   ```shell
   dotnet build -c Release
   dotnet test
   dotnet csharpier check .
   dotnet format style --verify-no-changes
   dotnet format analyzers --verify-no-changes
   ```
4. Open a Pull Request against `main` with a clear description of the change.

## Code Style

This project enforces formatting via **CSharpier** and **`dotnet format`**. CI will reject PRs with formatting violations.

- **Auto-format before committing**: `dotnet csharpier format .` first, then `dotnet format style && dotnet format analyzers`. **Order matters**: always run CSharpier first. Never run bare `dotnet format`.
- IDE integration: Install the CSharpier extension for [VS Code](https://marketplace.visualstudio.com/items?itemName=csharpier.csharpier-vscode) or [Visual Studio](https://marketplace.visualstudio.com/items?itemName=csharpier.CSharpier).

## Reporting Issues

Use [GitHub Issues](https://github.com/Bia10/Maple.StringPool/issues) for bugs and feature requests.

set windows-shell := ["pwsh.exe", "-NoLogo", "-Command"]
set shell := ["bash", "-c"]

solution        := "WithLoveShop.slnx"
configuration   := "Debug"

# List available recipes
default:
    @just --list

# Show project info
info:
    @echo "Solution  : {{solution}}"
    @echo "Config    : {{configuration}}"

# Remove all build output
clean:
    dotnet clean {{solution}} --configuration {{configuration}} --nologo -v q
    @echo "Clean complete."

# Restore NuGet packages
restore:
    dotnet restore {{solution}}

# Build the solution
build: restore
    dotnet build {{solution}} --configuration {{configuration}} --no-restore

# Alias: build
compile: build

# Start the full application stack via Aspire AppHost
run:
    dotnet run --project src/WithLove.AppHost

# Run all tests
test:
    dotnet test tests/WithLove.ProductsAPI.Tests/WithLove.ProductsAPI.Tests.csproj --logger "console;verbosity=normal"

# Run unit tests only (fast, no Docker required)
test-unit:
    dotnet test tests/WithLove.ProductsAPI.Tests/WithLove.ProductsAPI.Tests.csproj \
        --filter "Category=Unit" \
        --logger "console;verbosity=normal"

# Run integration tests only (requires Docker for SQL Server + Redis)
test-integration:
    dotnet test tests/WithLove.ProductsAPI.Tests/WithLove.ProductsAPI.Tests.csproj \
        --filter "Category=Integration" \
        --logger "console;verbosity=normal"


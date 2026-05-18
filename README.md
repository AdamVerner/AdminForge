# AdminForge

Automatic admin panel generator for ASP.NET Core applications.

## Getting started

```csharp
builder.Services.AddAdminForge(options =>
{
    options.Title = "My App Admin";
    options.RoutePrefix = "admin";
});

app.UseAdminForge();
```

## Installation

### NuGet

```
dotnet add package AdminForge
```

### As a git submodule

```bash
git submodule add https://github.com/averner/AdminForge vendor/AdminForge
```

Then reference the project directly:

```xml
<ProjectReference Include="vendor/AdminForge/src/AdminForge/AdminForge.csproj" />
```

## Running examples

```bash
dotnet run --project examples/TodoApp
```

## Running tests

```bash
dotnet test
```

## Building a NuGet package

```bash
dotnet pack src/AdminForge -c Release -o artifacts/
```

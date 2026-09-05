# Releasing

A `v*` tag is the release. CI (`.github/workflows/ci.yml`, job `publish`) builds and tests it, packs
`AdminForge` with the tag as the version, and pushes to GitHub Packages and nuget.org.

1. Set `<Version>` in `src/AdminForge/AdminForge.csproj`.
2. Push master, then tag it:

   ```
   git tag v0.2.0 && git push origin v0.2.0
   ```

3. Watch the `CI` run; the package is on nuget.org when `publish` is green.

Notes:

- nuget.org is reached by trusted publishing, gated by the GitHub `release` environment whose deployment rule allows `v*` tags only. 
- Pushes use `--skip-duplicate`: a version already on the feed is skipped, so a tag never re-publishes.
- The tag wins over `<Version>` in the csproj; the csproj value is what a local `dotnet pack` uses.

# Releasing

A `v*` tag is the release. CI (`.github/workflows/ci.yml`, job `publish`) builds and tests it. It
packs `AdminForge`, `AdminForge.EntityFrameworkCore` and `AdminForge.Mcp` with the tag as the
version, and pushes them to GitHub Packages and nuget.org.

1. Push master, then tag it:

   ```
   git tag v0.7.0 && git push origin v0.7.0
   ```

2. Watch the `CI` run. When `publish` is green, the packages are on nuget.org.

Notes:

- The tag is the only source of the version. A local `dotnet pack` needs `-p:Version=...`, or it
  packs 1.0.0.
- nuget.org is reached by trusted publishing. The GitHub `release` environment gates it, and its
  deployment rule allows `v*` tags only.
- Pushes use `--skip-duplicate`. The push skips a version already on the feed, so a tag never
  publishes twice.

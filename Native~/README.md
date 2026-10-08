# Native core source (optional submodule)

This folder is reserved for a git submodule (or local checkout) of **[3OS](https://github.com/scifiuiguy/3OS)** — the C++ `3os_kernel` sources.

## Suggested submodule setup

```bash
# from the 3OS_Unity repo root
git submodule add https://github.com/scifiuiguy/3OS.git Native~/3os-core
git submodule update --init --recursive
```

Until the submodule is added, build core from the sibling checkout:

```text
../   (or your clone of scifiuiguy/3OS)
```

See [docs/NATIVE_BINARIES.md](../docs/NATIVE_BINARIES.md) for copying build outputs into `Plugins/`.

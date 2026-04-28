# ModularMedpouch

> A modular pouch used for quick healing with medical items

**Author:** Manimal  
**Version:** 1.0.0  
**SPT Version:** 4.0.13  
**License:** MIT

---

## What This Mod Does

Describe what your mod does here.

This template includes a Harmony patch example using SPT's `ModulePatch` system. Patches are located in the `Patches/` folder and enabled in `Plugin.cs`.

### How Patches Work

1. Create a class that inherits `ModulePatch`.
2. Override `GetTargetMethod()` to specify which game method to patch.
3. Add `[PatchPrefix]` and/or `[PatchPostfix]` static methods.
4. Enable the patch in `Plugin.Awake()` with `new YourPatch().Enable()`.

---

## Requirements

- [SPT](https://www.sp-tarkov.com/) installed (BepInEx is included)
- .NET SDK (`netstandard2.1`-compatible) for building from source

---

## Building

```sh
cd ModularMedpouch
dotnet build -c Release
```

The PostBuild target automatically copies the compiled DLL to your SPT installation's `BepInEx\plugins\` folder.
The project also packages the mod into a distributable `ModularMedpouch.zip` ready to be uploaded.

---

## Installation

1. Build the project (see above) **or** download the latest release DLL.
2. Copy `ModularMedpouch.dll` to `<SPT install>\BepInEx\plugins\`.
3. Launch SPT as usual.

---

## Project Structure

```
ModularMedpouch/
├── ModularMedpouch.csproj   <- project file with DLL references
├── Plugin.cs              <- BepInEx plugin entry point
├── Patches/
│   └── ExamplePatch.cs    <- Harmony patch example (Prefix/Postfix)
├── README.md
└── .gitignore
```

---

## Learning Resources

| Resource | URL |
|---|---|
| SPT Client Mod Examples | https://github.com/Jehree/SPTClientModExamples |
| Harmony Patching Docs | https://harmony.pardeike.net/articles/patching.html |
| SPT Wiki Modding Resources | https://wiki.sp-tarkov.com/modding/Modding_Resources |

---

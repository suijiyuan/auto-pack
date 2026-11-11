# Third-party components

AutoPack uses an unmodified standalone 7-Zip console executable as a separate process.

- Component: 7-Zip Extra 26.06, Windows x64 `7za.exe`
- Copyright (C) 1999-2026 Igor Pavlov
- Main license: GNU LGPL 2.1 or later; some portions use BSD 2-clause or BSD 3-clause licenses.
- Full upstream license notices: `tools/7zip/License.txt` in source, `tools/7zip-License.txt` in the published package.
- Official site and source downloads: https://www.7-zip.org/
- Exact release and corresponding source: https://github.com/ip7z/7zip/releases/tag/26.06
- Binary archive: https://github.com/ip7z/7zip/releases/download/26.06/7z2606-extra.7z
- Archive SHA256: `396c180f12d2247c24b0b1fcd482c6fc2a2700f61211ffede1061dd971f5e104`
- Included executable SHA256: `dd3757f31c97e33776f2590317d4fb12f2da075db3dbb1e3f94d0f2a01d84ffa`

7-Zip is free software. It is distributed without warranty under the terms in the upstream license file. It can be replaced independently of AutoPack; the publish script validates the pinned original binary for reproducible packaging.

The self-contained publish also includes Microsoft .NET runtime components. The publish script copies license and third-party notices from the resolved runtime packages to `licenses/` in the published package. .NET source and license: https://github.com/dotnet/runtime and https://github.com/dotnet/winforms.

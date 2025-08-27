name: Build & Release Battery Overlay

on:
  push:
    tags:
      - "v*" # e.g., v1.0.3
  workflow_dispatch: # Manual run from button

permissions:
  contents: write # Required for Release

jobs:
  build:
    runs-on: windows-latest

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Restore dependencies
        run: dotnet restore

      - name: Publish (single-file exe)
        run: dotnet publish OverlayApp.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true

      - name: Create zip
        shell: pwsh
        run: |
          Compress-Archive -Path "bin/Release/net8.0-windows/win-x64/publish/*" -DestinationPath "OverlayApp-v1.0.3.zip" -Force

      - name: Create GitHub Release
        uses: softprops/action-gh-release@v2
        with:
          files: |
            bin/Release/net8.0-windows/win-x64/publish/OverlayApp.exe
            OverlayApp-v1.0.3.zip
          name: Version 1.0.3
          tag_name: v1.0.3
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}

# MoreProducts Texture Optimiser

Reduces the size of MoreProducts textures to reduce memory usage on systems without large amounts of available memory.

Will block game window from opening until finished, so may take 15-20 seconds on first boot. This is reduced to 1-2 seconds on subsequent boots.

Original textures are backed up to `BepinEx/backup_textures/`` and can be copied back to restore.

Default behaviour:
- Resizes all product icons to 512 pixels on their longest side
- Resizes object textures to factors of 1024 pixels on their longest side
  - Every 1000 units of surface area in a products box increases this, so larger product boxes will often use 2048 pixels instead

Dependant on SkiaSharp.dll and libSkiaSharp.dll being present alongside the produced dll. Any contributions to package these inside are welcome.

## Build

`dotnet build`

Copy resulting `MoreProductsTextureOptimiser.dll`` to plugins folder.

## Release

Creates a `MoreProductsTextureOptimiser.zip`` in the project folder.

`dotnet publish --configuration Release`
using BepInEx;
using BepInEx.Logging;
using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json;
using SkiaSharp; // Replacing ImageSharp with SkiaSharp
using System.Threading.Tasks;
using System.Threading;

namespace TextureOptimiser
{
    [BepInPlugin("MoreProductsTextureOptimiser", "MoreProductsTextureOptimiser", "1.0.0")]
    [BepInDependency("MoreProducts")]
    public class Plugin : BaseUnityPlugin
    {
        internal static new ManualLogSource Logger;

        private const int MAX_PRODUCT_ICON_SIZE = 512;
        private const int BASE_OBJECT_TEXTURE_SIZE = 1024;
        private const int SURFACE_SIZE_FACTOR = 1000;

        private void Awake()
        {
            Logger = base.Logger;
            Logger.LogInfo("Plugin MoreProductsTextureOptimiser is loaded!");
        }

        private void OnEnable()
        {
            RunTextureOptimization();
        }

        private void RunTextureOptimization()
        {
            string baseDir = Path.Combine(Paths.PluginPath, "MoreProducts");
            if (Directory.Exists(baseDir)) {
                var stopwatch = Stopwatch.StartNew();
                string backupDir = Path.Combine(baseDir, "../backup_textures");

                // Read through all product packs to find textures to resize
                foreach (var dirPath in Directory.GetDirectories(baseDir, "*", SearchOption.AllDirectories))
                {
                    ProcessProductPack(dirPath, baseDir, backupDir);
                }

                stopwatch.Stop();
                Logger.LogInfo($"Texture optimisation completed in {stopwatch.ElapsedMilliseconds} ms!");
            } else {
                Logger.LogError("MoreProducts directory does not exist?");
            }
        }

        private void ProcessProductPack(string dirPath, string rootDirectory, string backupDirectory)
        {
            string jsonPath = Path.Combine(dirPath, "products.json");
            if (File.Exists(jsonPath))
            {
                int productCount = 0;
                int textureCount = 0;
                int resizedTextureCount = 0;
                int maxSurfaceSize = 0;

                // Find largest box size in list of products
                // This helps scale the resolution higher for larger products, with the assumption they'll be in large boxes
                string jsonContent = File.ReadAllText(jsonPath);
                var data = JsonConvert.DeserializeObject<ProductLicenseData>(jsonContent);
                foreach (var license in data.ProductLicenses)
                {
                    foreach (var product in license.Products)
                    {
                        productCount++;
                        string boxSize = product.GridLayoutInBox.boxSize.ToString();
                        int surfaceSize = CalculateSurfaceSize(boxSize);
                        maxSurfaceSize = Math.Max(maxSurfaceSize, surfaceSize);
                    }
                }
                var maxScale = CalculateMaxScale(maxSurfaceSize);

                // Resize object textures
                string texturesDir = Path.Combine(dirPath, "objects_textures");
                if (Directory.Exists(texturesDir))
                {
                    var textureFiles = Directory.GetFiles(texturesDir);
                    textureCount += textureFiles.Length;

                    Parallel.ForEach(textureFiles, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, textureFile =>
                    {
                        if (ProcessImage(textureFile, maxScale, backupDirectory, rootDirectory))
                        {
                            Interlocked.Increment(ref resizedTextureCount);
                        }
                    });
                }

                // Resize product icons
                string iconsDir = Path.Combine(dirPath, "products_icons");
                if (Directory.Exists(iconsDir))
                {
                    var iconFiles = Directory.GetFiles(iconsDir);
                    textureCount += iconFiles.Length;

                    Parallel.ForEach(iconFiles, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, iconFile =>
                    {
                        if (ProcessImage(iconFile, MAX_PRODUCT_ICON_SIZE, backupDirectory, rootDirectory))
                        {
                            Interlocked.Increment(ref resizedTextureCount);
                        }
                    });
                }

                string relProdPath = Path.GetRelativePath(rootDirectory, dirPath);
                Logger.LogInfo($"-- {relProdPath} --");
                Logger.LogInfo($"  Products: {productCount}");
                Logger.LogInfo($"  Textures: {textureCount}");
                Logger.LogInfo($"  Resized Textures: {resizedTextureCount}");
            }
        }

        private bool ProcessImage(string imagePath, int maxScale, string backupDirectory, string rootDirectory)
        {
            if (File.Exists(imagePath))
            {
                try
                {
                    // Open original image
                    using var inputStream = File.OpenRead(imagePath);
                    using var originalBitmap = SKBitmap.Decode(inputStream);

                    // Check width and height to see if we need to resize
                    int width = originalBitmap.Width;
                    int height = originalBitmap.Height;

                    if (width > maxScale || height > maxScale)
                    {
                        string relativePath = Path.GetRelativePath(rootDirectory, imagePath);

                        // Create the backup path and directory
                        string backupImagePath = Path.Combine(backupDirectory, relativePath);
                        Directory.CreateDirectory(Path.GetDirectoryName(backupImagePath));

                        // Copy the original image to the backup directory
                        File.Copy(imagePath, backupImagePath, true);

                        // Find the factor to scale by so the longest side is capped by the max scale
                        float widthScale = (float)maxScale / width;
                        float heightScale = (float)maxScale / height;
                        float scaleFactor = Math.Min(widthScale, heightScale);

                        int newWidth = (int)(width * scaleFactor);
                        int newHeight = (int)(height * scaleFactor);

                        // Resize the image
                        using var resizedBitmap = originalBitmap.Resize(new SKImageInfo(newWidth, newHeight), SKFilterQuality.High);
                        if (resizedBitmap == null)
                        {
                            Logger.LogWarning($"  Failed to resize image: {imagePath}");
                            return false;
                        }

                        Logger.LogInfo($"  Resized Image: ({width}x{height}) -> ({newWidth}x{newHeight}) [{Path.GetRelativePath(rootDirectory, imagePath)}]");

                        // Save the resized image
                        SKEncodedImageFormat format = GetImageFormat(imagePath);
                        using var image = SKImage.FromBitmap(resizedBitmap);
                        using var outputStream = new FileStream(imagePath, FileMode.Create, FileAccess.Write);
                        image.Encode(format, 90).SaveTo(outputStream);

                        return true;
                    }
                }
                catch (Exception e)
                {
                    Logger.LogError($"  ERROR processing image {imagePath}: {e.Message}");
                }
            }
            else
            {
                Logger.LogWarning($"  Image not found: {imagePath}");
            }

            return false;
        }

        private SKEncodedImageFormat GetImageFormat(string imagePath)
        {
            string extension = Path.GetExtension(imagePath).ToLowerInvariant();
            return extension switch
            {
                ".png" => SKEncodedImageFormat.Png,
                ".jpg" or ".jpeg" => SKEncodedImageFormat.Jpeg,
                _ => SKEncodedImageFormat.Jpeg // Default to Jpeg if unknown format
            };
        }

        private static int CalculateSurfaceSize(string boxSize)
        {
            string[] dimensions = boxSize.Trim('_').Split('x');
            int length = int.Parse(dimensions[0]);
            int width = int.Parse(dimensions[1]);
            int height = int.Parse(dimensions[2]);
            int surfaceArea = 2 * (length * width + width * height + height * length);
            return surfaceArea;
        }

        private static int CalculateMaxScale(int surfaceSize)
        {
            int scalingFactor = surfaceSize / SURFACE_SIZE_FACTOR * BASE_OBJECT_TEXTURE_SIZE;
            if (scalingFactor == 0)
                scalingFactor = BASE_OBJECT_TEXTURE_SIZE;
            return scalingFactor;
        }
    }

    public class ProductLicenseData
    {
        public List<ProductLicense> ProductLicenses { get; set; }
    }

    public class ProductLicense
    {
        public List<Product> Products { get; set; }
    }

    public class Product
    {
        public GridLayoutInBox GridLayoutInBox { get; set; }
    }

    public class GridLayoutInBox
    {
        public string boxSize { get; set; }
    }
}

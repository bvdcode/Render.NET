using System;
using System.Globalization;
using System.Resources;

namespace Render.NET.Resources
{
    internal static class BundledResources
    {
        private static readonly ResourceManager Manager = new(typeof(BundledResources));

        internal static byte[] Read(string name)
        {
            return Manager.GetObject(name, CultureInfo.InvariantCulture) as byte[]
                ?? throw new InvalidOperationException("Bundled resource is missing: " + name);
        }
    }
}

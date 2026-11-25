using System.Runtime.CompilerServices;
using System.Windows;

namespace PD2Launcherv2.Resources.Styles
{
    public static class ResourceKeys
    {
        public static ResourceKey PixelPerfectFrameworkElementBase { get; } = CreateResourceKey();
        public static ResourceKey PixelPerfectFrameworkElementWithClearText { get; } = CreateResourceKey();
        public static ResourceKey PixelPerfectFrameworkElement { get; } = CreateResourceKey();

        private static ComponentResourceKey CreateResourceKey([CallerMemberName] string caller = null!)
        {
            return new ComponentResourceKey(typeof(ResourceKeys), caller);
        }
    }
}

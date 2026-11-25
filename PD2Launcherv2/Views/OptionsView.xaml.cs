using GalaSoft.MvvmLight.Messaging;
using PD2Launcherv2.Messages;
using PD2Launcherv2.Resources.Styles;
using PD2Shared.Utils;
using System.Windows;
using System.Windows.Controls;

namespace PD2Launcherv2.Views
{
    /// <summary>
    /// Interaction logic for OptionsView.xaml
    /// </summary>
    public partial class OptionsView : Page
    {
        public OptionsView()
        {
            InitializeComponent();

            Messenger.Default.Register<LauncherOptionsChangeMessage>(this, m =>
            {
                // This message is expected to be received after MainWindow, which will have already taken care of updating
                // the resources. Thus, all that is left here is to apply the updated style.
                this.Style = (Style)Application.Current.Resources[ResourceKeys.PixelPerfectFrameworkElement];
            });

            // It's much easier to have this set up here than via the model
            if (Wine.IsRunningUnderWine)
            {
                WindowsPermissionsText.Text = "Wine configuration";
                SetWindowsPermissions.Content = "Apply";
                SetWindowsPermissions.ToolTip = null;
                RemoveWindowsPermissions.Content = "Remove";
                RemoveWindowsPermissions.ToolTip = null;
            }
            else
            {
                WineLogo32Image.Visibility = System.Windows.Visibility.Collapsed;
                WineLogo32ImageCopy.Visibility = System.Windows.Visibility.Collapsed;
            }
        }
    }
}
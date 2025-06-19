// ********************************************************************************************************************
//
// MainInterfacePage.xaml.cs -- ui c# for main interface page
//
// Copyright(C) 2025 ilominar/raven
//
// This program is free software: you can redistribute it and/or modify it under the terms of the GNU General
// Public License as published by the Free Software Foundation, either version 3 of the License, or (at your
// option) any later version.
//
// This program is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the
// implied warranty of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU General Public License
// for more details.
//
// You should have received a copy of the GNU General Public License along with this program.  If not, see
// <https://www.gnu.org/licenses/>.
//
// ********************************************************************************************************************

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.IO;
using ViperOps2MIZ.Utility.Files;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;
using Windows.Storage;
using WinRT.Interop;

namespace ViperOps2MIZ.UI
{
    /// <summary>
    /// TODO - document
    /// </summary>
    public sealed partial class MainInterfacePage : Page
    {
        // ------------------------------------------------------------------------------------------------------------
        //
        // properties
        //
        // ------------------------------------------------------------------------------------------------------------

        private string? PathMiz { get; set; }

        private string? PathKml { get; set; }

        // ------------------------------------------------------------------------------------------------------------
        //
        // construction
        //
        // ------------------------------------------------------------------------------------------------------------

        public MainInterfacePage()
        {
            InitializeComponent();

            RebuildUI();
        }

        // ------------------------------------------------------------------------------------------------------------
        //
        // ui management
        //
        // ------------------------------------------------------------------------------------------------------------

        public void RebuildUI()
        {
            uiTemplatePath.Text = PathMiz;
            uiViperOpsPath.Text = PathKml;

            uiBtnProcessKML.IsEnabled = !string.IsNullOrEmpty(PathMiz) && !string.IsNullOrEmpty(PathKml);
        }

        /// <summary>
        /// TODO - document
        /// </summary>
        private async void ConvertKML2MIZ(string pathKml, string pathMiz)
        {
            FileSavePicker picker = new()
            {
                SettingsIdentifier = "ViperOps2MIZ_SelectTemplate",
                SuggestedStartLocation = PickerLocationId.Desktop,
                SuggestedFileName = $"{Path.GetFileNameWithoutExtension(pathKml)}.miz"
            };
            picker.FileTypeChoices.Add("DCS Mission", [".miz"]);
            var hwnd = WindowNative.GetWindowHandle((Application.Current as ViperOps2MIZ.App)?.Window);
            InitializeWithWindow.Initialize(picker, hwnd);

            StorageFile file = await picker.PickSaveFileAsync();

            if ((file != null) && !string.IsNullOrEmpty(pathKml) && !string.IsNullOrEmpty(pathMiz))
            {
                try
                {
                    FileKml kml = new(pathKml);
                    FileMiz miz = new(pathMiz);
                    miz.BuildMissionWithKml(kml);
                    miz.SaveMission(file.Path);

                    ContentDialog dialog = new ContentDialog()
                    {
                        XamlRoot = Content.XamlRoot,
                        Title = "Success",
                        Content = $"Mission construction successful. Saved to {file.Path}",
                        PrimaryButtonText = "OK",
                        DefaultButton = ContentDialogButton.Primary
                    };
                    ContentDialogResult result = await dialog.ShowAsync(ContentDialogPlacement.Popup);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                    ContentDialog dialog = new ContentDialog()
                    {
                        XamlRoot = Content.XamlRoot,
                        Title = "Bummer...",
                        Content = $"There was an error while processing files: {ex.Message}",
                        PrimaryButtonText = "OK",
                        DefaultButton = ContentDialogButton.Primary
                    };
                    ContentDialogResult result = await dialog.ShowAsync(ContentDialogPlacement.Popup);
                }
            }
        }

        // ------------------------------------------------------------------------------------------------------------
        //
        // ui interactions
        //
        // ------------------------------------------------------------------------------------------------------------

        // ---- drag & drop -------------------------------------------------------------------------------------------

        /// <summary>
        /// handle drag over the main panel in the ui by letting windoze know we can do copies.
        /// </summary>
        private void MainPanel_DragOver(object sender, DragEventArgs e)
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.IsCaptionVisible = false;
            e.DragUIOverride.IsGlyphVisible = true;
        }

        /// <summary>
        /// handle drop over the main panel by mapping a .miz file to the template and a .kml file to the kml file.
        /// </summary>
        private async void MainPanel_Drop(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                foreach (var item in items)
                {
                    StorageFile? file = item as StorageFile;
                    if ((file != null) && string.Equals(Path.GetExtension(file.Path), ".miz"))
                    {
                        PathMiz = file.Path;
                        // TODO: persist template path to settings?
                        RebuildUI();
                    }
                    else if ((file != null) && string.Equals(Path.GetExtension(file.Path), ".kml"))
                    {
                        PathKml = file.Path;
                        RebuildUI();
                    }
                    else
                    {
                        break;
                    }
                }
            }
        }

        // ---- buttons -----------------------------------------------------------------------------------------------

        /// <summary>
        /// TODO - document
        /// </summary>
        private async void BtnSelectTemplate_Click(object sender, RoutedEventArgs e)
        {
            FileOpenPicker picker = new()
            {
                SettingsIdentifier = "ViperOps2MIZ_SelectTemplate",
                SuggestedStartLocation = PickerLocationId.Desktop
            };
            picker.FileTypeFilter.Add(".miz");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle((Application.Current as ViperOps2MIZ.App)?.Window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            StorageFile file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                PathMiz = file.Path;
                // TODO: persist template path to settings?
                RebuildUI();
            }
        }

        /// <summary>
        /// TODO - document
        /// </summary>
        private async void BtnSelectViperOps_Click(object sender, RoutedEventArgs e)
        {
            FileOpenPicker picker = new()
            {
                SettingsIdentifier = "ViperOps2MIZ_SelectViperOps",
                SuggestedStartLocation = PickerLocationId.Desktop
            };
            picker.FileTypeFilter.Add(".kml");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle((Application.Current as ViperOps2MIZ.App)?.Window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            StorageFile file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                PathKml = file.Path;
                RebuildUI();
            }
        }

        /// <summary>
        /// TODO - document
        /// </summary>
        private void BtnuiBtnProcessKML_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(PathKml) && !string.IsNullOrEmpty(PathMiz))
                ConvertKML2MIZ(PathKml, PathMiz);
        }
    }
}

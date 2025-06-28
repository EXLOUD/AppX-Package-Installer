using AppxInstaller.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AppxInstaller
{
    public partial class MainWindow : Window
    {
        private readonly string[] DependencyPatterns = { "VCLibs", "NET", "Xaml", "WinJS" };
        private readonly string[] AppxExtensions = { ".appx", ".appxbundle", ".msix", ".msixbundle" };

        private List<PackageFileModel> _packageFiles = new List<PackageFileModel>();
        private CancellationTokenSource _cancellationTokenSource;

        private bool _isInstalling = false;
        private int _totalPackages = 0;
        private int _installedPackages = 0;

        private Storyboard _loadingAnimation;
        private Storyboard _checkmarkAnimation;
        private Storyboard _hideCheckmarkAnimation;
        private Storyboard _successFadeOutAnimation;
        private Storyboard _emptyFadeInAnimation;

        public MainWindow()
        {
            InitializeComponent();
            InitializeApplication();
        }

        private void InitializeApplication()
        {
            InitializeAnimations();
            UpdateUIWithFadeIn();
        }

        private void InitializeAnimations()
        {
            _loadingAnimation = (Storyboard)Resources["LoadingAnimation"];
            _checkmarkAnimation = (Storyboard)Resources["CheckmarkAnimation"];
            _hideCheckmarkAnimation = (Storyboard)Resources["HideCheckmarkAnimation"];
            _successFadeOutAnimation = (Storyboard)Resources["SuccessStatesFadeOutAnimation"];
            _emptyFadeInAnimation = (Storyboard)Resources["EmptyStateFadeInAnimation"];
        }

        #region UI State Management

        private void ShowEmptyState()
        {
            StopAllAnimations();
            ResetAnimationStates();

            EmptyStatePanel.Visibility = Visibility.Visible;
            EmptyStatePanel.Opacity = 1;
            LoadingStatePanel.Visibility = Visibility.Collapsed;
            SuccessStatePanel.Visibility = Visibility.Collapsed;
        }

        private void ShowLoadingState()
        {
            StopAllAnimations();

            EmptyStatePanel.Visibility = Visibility.Collapsed;
            LoadingStatePanel.Visibility = Visibility.Visible;
            SuccessStatePanel.Visibility = Visibility.Collapsed;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                _loadingAnimation.Begin();
            }), DispatcherPriority.Loaded);
        }

        private void ShowSuccessState()
        {
            EmptyStatePanel.Visibility = Visibility.Collapsed;
            LoadingStatePanel.Visibility = Visibility.Collapsed;
            SuccessStatePanel.Visibility = Visibility.Visible;
            SuccessStatePanel.Opacity = 1;

            _loadingAnimation.Stop();

            SuccessCheckmark.Opacity = 0;
            SuccessCheckmark.RenderTransform = new System.Windows.Media.ScaleTransform(0.5, 0.5);

            Dispatcher.BeginInvoke(new Action(() =>
            {
                _checkmarkAnimation.Begin();
            }), DispatcherPriority.Loaded);
        }

        private void StartFadeToEmptyState()
        {
            if (SuccessStatePanel.Visibility == Visibility.Visible)
            {
                _successFadeOutAnimation.Begin();
            }
            else
            {
                ShowEmptyState();
            }
        }

        private void SuccessStatesFadeOutAnimation_Completed(object sender, EventArgs e)
        {
            SuccessStatePanel.Visibility = Visibility.Collapsed;
            EmptyStatePanel.Visibility = Visibility.Visible;
            EmptyStatePanel.Opacity = 0;

            _emptyFadeInAnimation.Begin();
        }

        private void StopAllAnimations()
        {
            try
            {
                _loadingAnimation?.Stop();
                _checkmarkAnimation?.Stop();
                _hideCheckmarkAnimation?.Stop();
                _successFadeOutAnimation?.Stop();
                _emptyFadeInAnimation?.Stop();
            }
            catch (Exception ex)
            {
                LogMessage($"Error stopping animations: {ex.Message}");
            }
        }

        private void ResetAnimationStates()
        {
            try
            {
                if (LoadingSpinner?.RenderTransform is System.Windows.Media.RotateTransform rotateTransform)
                {
                    rotateTransform.Angle = 0;
                }

                if (SuccessCheckmark != null)
                {
                    SuccessCheckmark.Opacity = 0;
                    if (SuccessCheckmark.RenderTransform is System.Windows.Media.ScaleTransform scaleTransform)
                    {
                        scaleTransform.ScaleX = 0.5;
                        scaleTransform.ScaleY = 0.5;
                    }
                }

                if (SuccessStatePanel != null)
                {
                    SuccessStatePanel.Opacity = 1;
                }

                if (EmptyStatePanel != null)
                {
                    EmptyStatePanel.Opacity = 1;
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error resetting animation states: {ex.Message}");
            }
        }

        private void UpdateInstallationStatus(string message)
        {
            Dispatcher.Invoke(() =>
            {
                if (InstallationStatusText != null)
                {
                    InstallationStatusText.Text = message;
                }
            });
        }

        #endregion

        private void Border_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://github.com/EXLOUD",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"err: {ex.Message}", "Error",
                               MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        #region Custom Window Chrome Event Handlers

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            this.DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        #endregion

        #region Drag and Drop Support

        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            if (_isInstalling)
            {
                e.Effects = DragDropEffects.None;
                return;
            }

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Any(f => AppxExtensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase))))
                {
                    e.Effects = DragDropEffects.Copy;
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            Window_DragEnter(sender, e);
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            if (_isInstalling)
            {
                LogMessage("Installation already in progress. Please wait...");
                return;
            }

            if (!e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            var appxFiles = files.Where(f => AppxExtensions.Any(ext =>
                f.EndsWith(ext, StringComparison.OrdinalIgnoreCase))).ToList();

            if (appxFiles.Count == 0)
            {
                return;
            }

            try
            {
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();

                LoadPackagesFromFiles(appxFiles);

                LogMessage($"Автоматична установка для {appxFiles.Count} файлів");

                ShowLoadingState();

                await Task.Delay(200);

                UpdateInstallationStatus($"Preparing to install {appxFiles.Count} packages...");

                await StartInstallationAsync();
            }
            catch (Exception ex)
            {
                LogMessage($"Error handling drop: {ex.Message}");
                ShowEmptyState();
            }
        }

        #endregion

        #region Package Loading

        private void LoadPackagesFromFiles(List<string> filePaths)
        {
            _packageFiles.Clear();

            foreach (string filePath in filePaths)
            {
                if (!File.Exists(filePath))
                    continue;

                var packageFile = new PackageFileModel
                {
                    FileName = Path.GetFileName(filePath),
                    FullPath = filePath,
                    Type = DeterminePackageType(Path.GetFileName(filePath)),
                    ShowCheckbox = false,
                    IsSelected = true
                };

                _packageFiles.Add(packageFile);
            }

            LogMessage($"Loaded {_packageFiles.Count} packages");
        }

        private PackageType DeterminePackageType(string fileName)
        {
            if (fileName.EndsWith(".appxbundle", StringComparison.OrdinalIgnoreCase) ||
                fileName.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase))
            {
                return AppxInstaller.Models.PackageType.Bundle;
            }
            else if (fileName.EndsWith(".appx", StringComparison.OrdinalIgnoreCase) ||
                     fileName.EndsWith(".msix", StringComparison.OrdinalIgnoreCase))
            {
                if (DependencyPatterns.Any(pattern =>
                    fileName.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return AppxInstaller.Models.PackageType.Dependency;
                }
                else
                {
                    return AppxInstaller.Models.PackageType.MainApp;
                }
            }
            return AppxInstaller.Models.PackageType.MainApp;
        }

        #endregion

        #region UI Updates

        private void UpdateUIWithFadeIn()
        {
            ShowEmptyStateWithFadeIn();

            var selectedFiles = _packageFiles.Count(f => f.IsSelected);
            _totalPackages = selectedFiles;
        }

        private void ShowEmptyStateWithFadeIn()
        {
            StopAllAnimations();

            ResetAnimationStates();

            EmptyStatePanel.Visibility = Visibility.Visible;
            EmptyStatePanel.Opacity = 0;
            LoadingStatePanel.Visibility = Visibility.Collapsed;
            SuccessStatePanel.Visibility = Visibility.Collapsed;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                _emptyFadeInAnimation.Begin();
            }), DispatcherPriority.Loaded);
        }

        #endregion

        private void GitHubButton_Click(object sender, RoutedEventArgs e)
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/EXLOUD") { UseShellExecute = true });
        }


        #region Installation Logic

        private async Task StartInstallationAsync()
        {
            if (_isInstalling)
            {
                LogMessage("Installation already in progress");
                return;
            }

            var selectedFiles = _packageFiles.Where(f => f.IsSelected).ToList();
            if (selectedFiles.Count == 0)
            {
                LogMessage("No packages selected for installation");
                ShowEmptyState();
                return;
            }

            _isInstalling = true;
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                LogMessage($"Starting installation of {selectedFiles.Count} packages...");
                await InstallPackagesAsync(_cancellationTokenSource.Token);
                LogMessage("Installation completed successfully!");

                ShowSuccessState();

                _packageFiles.Clear();

                await Task.Delay(3000, _cancellationTokenSource.Token);

                Dispatcher.Invoke(() =>
                {
                    StartFadeToEmptyState();
                });
            }
            catch (OperationCanceledException)
            {
                LogMessage("Installation was cancelled");
                UpdateInstallationStatus("Installation cancelled");

                await Task.Delay(1500);

                Dispatcher.Invoke(() =>
                {
                    ShowEmptyState();
                });
            }
            catch (Exception ex)
            {
                LogMessage($"Installation failed: {ex.Message}");
                UpdateInstallationStatus($"Installation failed: {ex.Message}");

                await Task.Delay(3000);

                Dispatcher.Invoke(() =>
                {
                    ShowEmptyState();
                });
            }
            finally
            {
                _isInstalling = false;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private async Task InstallPackagesAsync(CancellationToken cancellationToken)
        {
            _installedPackages = 0;
            var selectedFiles = _packageFiles.Where(f => f.IsSelected).ToList();

            var sortedFiles = selectedFiles
                .OrderBy(f => f.Type == PackageType.Dependency ? 0 :
                             f.Type == PackageType.MainApp ? 1 : 2)
                .ToList();

            foreach (var packageFile in sortedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                UpdateInstallationStatus($"Installing {packageFile.FileName}... ({_installedPackages + 1}/{selectedFiles.Count})");

                await InstallSinglePackageAsync(packageFile, cancellationToken);
                _installedPackages++;
            }
        }

        private async Task InstallSinglePackageAsync(PackageFileModel packageFile, CancellationToken cancellationToken)
        {
            try
            {
                if (!File.Exists(packageFile.FullPath))
                {
                    LogMessage($"File not found: {packageFile.FileName}");
                    return;
                }

                if (!CheckArchitecture(packageFile.FileName))
                {
                    LogMessage($"Skipped (architecture mismatch): {packageFile.FileName}");
                    return;
                }

                string licenseFile = FindLicenseFile(packageFile.FullPath);

                LogMessage($"Installing: {packageFile.FileName}");

                bool success = await InstallPackageViaPowerShellAsync(packageFile.FullPath, licenseFile, cancellationToken);

                if (success)
                {
                    LogMessage($"✓ Successfully installed: {packageFile.FileName}");
                }
                else
                {
                    LogMessage($"✗ Failed to install: {packageFile.FileName}");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Error installing {packageFile.FileName}: {ex.Message}");
            }
        }

        private bool CheckArchitecture(string fileName)
        {
            bool is64BitOS = Environment.Is64BitOperatingSystem;

            if (!is64BitOS)
            {
                return !fileName.Contains("_x64_") && !fileName.Contains("_arm_") && !fileName.Contains("_arm64_");
            }
            else
            {
                return !fileName.Contains("_arm_") && !fileName.Contains("_arm64_");
            }
        }

        private string FindLicenseFile(string packagePath)
        {
            try
            {
                string packageDir = Path.GetDirectoryName(packagePath);
                string packageName = Path.GetFileNameWithoutExtension(Path.GetFileName(packagePath)).Split('_')[0];
                string licensePattern = packageName + "*.xml";

                var licenseFiles = Directory.GetFiles(packageDir, licensePattern, SearchOption.TopDirectoryOnly);
                return licenseFiles.Length > 0 ? licenseFiles[0] : null;
            }
            catch
            {
                return null;
            }
        }

        private async Task<bool> InstallPackageViaPowerShellAsync(string packagePath, string licensePath, CancellationToken cancellationToken)
        {
            using (var runspace = RunspaceFactory.CreateRunspace())
            {
                runspace.Open();
                using (var powershell = PowerShell.Create())
                {
                    powershell.Runspace = runspace;

                    var script = $@"
                        try {{
                            {(licensePath != null ? $"Add-AppxProvisionedPackage -Online -PackagePath '{packagePath}' -LicensePath '{licensePath}'" : $"Add-AppxProvisionedPackage -Online -PackagePath '{packagePath}' -SkipLicense")} -ErrorAction Stop
                        }} catch {{
                            try {{
                                Add-AppxPackage -Path '{packagePath}' -ErrorAction Stop
                            }} catch {{
                                Write-Host 'ERROR:' $_.Exception.Message
                                exit 1
                            }}
                        }}";

                    powershell.AddScript(script);

                    try
                    {
                        var results = await Task.Run(() =>
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            return powershell.Invoke();
                        }, cancellationToken);

                        return !powershell.HadErrors;
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                }
            }
        }

        #endregion

        #region Logging

        private void LogMessage(string message)
        {
            Dispatcher.Invoke(() =>
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
            });
        }

        #endregion

        #region Cleanup

        protected override void OnClosed(EventArgs e)
        {
            try
            {
                _cancellationTokenSource?.Cancel();
                _cancellationTokenSource?.Dispose();
                StopAllAnimations();
            }
            catch (Exception ex)
            {
                LogMessage($"Error during cleanup: {ex.Message}");
            }

            base.OnClosed(e);
        }

        #endregion
    }
}
﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace MadsKristensen.ImageOptimizer
{

    [PackageRegistration(UseManagedResourcesOnly = true)]
    [InstalledProductRegistration("#110", "#112", Version, IconResourceID = 400)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideAutoLoad(UIContextGuids80.SolutionExists)]
    [Guid(GuidList.guidImageOptimizerPkgString)]
    public sealed class ImageOptimizerPackage : Package
    {
        public const string Version = "1.0";
        public DTE2 _dte;
        public static ImageOptimizerPackage Instance;
        private IEnumerable<string> _selectedPaths;

        protected override void Initialize()
        {
            base.Initialize();
            _dte = GetService(typeof(DTE)) as DTE2;
            Instance = this;

            OleMenuCommandService mcs = GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            CommandID cmdOptimize = new CommandID(GuidList.guidImageOptimizerCmdSet, (int)PackageCommands.cmdOptimizeImage);
            OleMenuCommand menuOptimize = new OleMenuCommand(OptimizeImage, cmdOptimize);
            menuOptimize.BeforeQueryStatus += MenuOptimizeBeforeQueryStatus;
            mcs.AddCommand(menuOptimize);
        }

        void MenuOptimizeBeforeQueryStatus(object sender, EventArgs e)
        {
            OleMenuCommand button = (OleMenuCommand)sender;
            _selectedPaths = GetSelectedFilePaths().Where(file => Compressor.IsFileSupported(file));

            int items = _selectedPaths.Count();

            button.Text = items == 1 ? "Optimize image" : "Optimize images";
            button.Enabled = items > 0;
        }

        private void OptimizeImage(object sender, EventArgs e)
        {
            string text = _selectedPaths.Count() == 1 ? " image" : " images";
            _dte.StatusBar.Text = "Optimizing " + _selectedPaths.Count() + text + "...";
            _dte.StatusBar.Animate(true, vsStatusAnimation.vsStatusAnimationDeploy);

            Compressor compressor = new Compressor();
            List<CompressionResult> list = new List<CompressionResult>();

            foreach (string file in _selectedPaths)
            {
                var result = compressor.CompressFile(file);
                HandleResult(result);

                if (result.Saving > 0)
                    list.Add(result);
            }

            _dte.StatusBar.Animate(false, vsStatusAnimation.vsStatusAnimationDeploy);

            if (list.Any())
                DisplayEndResult(list);
        }

        public IEnumerable<string> GetSelectedFilePaths()
        {
            return GetSelectedItemPaths()
                .SelectMany(p => Directory.Exists(p)
                                 ? Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories)
                                 : new[] { p }
                           );
        }

        public IEnumerable<string> GetSelectedItemPaths(DTE2 dte = null)
        {
            var items = (Array)_dte.ToolWindows.SolutionExplorer.SelectedItems;
            foreach (UIHierarchyItem selItem in items)
            {
                var item = selItem.Object as ProjectItem;

                if (item != null && item.Properties != null)
                    yield return item.Properties.Item("FullPath").Value.ToString();
            }
        }

        private void HandleResult(CompressionResult result)
        {
            string name = Path.GetFileName(result.OriginalFileName);

            if (result.Saving > 0)
            {
                if (_dte.SourceControl.IsItemUnderSCC(result.OriginalFileName) && !_dte.SourceControl.IsItemCheckedOut(result.OriginalFileName))
                    _dte.SourceControl.CheckOutItem(result.OriginalFileName);

                File.Copy(result.ResultFileName, result.OriginalFileName, true);

                string text = "Compressed " + name + " by " + result.Saving + " bytes / " + result.Percent + "%";
                _dte.StatusBar.Text = text;
                Logger.Log(result.ToString());
            }
            else
            {
                _dte.StatusBar.Text = name + " is already optimized";
            }
        }

        private void DisplayEndResult(List<CompressionResult> list)
        {
            long savings = list.Sum(r => r.Saving);
            long originals = list.Sum(r => r.OriginalFileSize);
            long results = list.Sum(r => r.ResultFileSize);

            if (savings > 0)
            {
                double percent = Math.Round(100 - ((double)results / (double)originals * 100), 1, MidpointRounding.AwayFromZero);
                _dte.StatusBar.Text = list.Count + " images optimized. Total saving of " + savings + " bytes / " + percent + "%";
            }
            else
            {
                _dte.StatusBar.Text = "The images were already optimized";
            }
        }
    }
}
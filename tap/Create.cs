using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Controls;

namespace tap
{
    class Create
    {
        private string CreatedPatchesDir = "CreatedPatches";
        private ListBox _lstFiles;
        public Create(ListBox lstFiles)
        {
            _lstFiles = lstFiles;
            if (!Directory.Exists(CreatedPatchesDir))
            {
                Directory.CreateDirectory(CreatedPatchesDir);
            }
        }

        public void Clear()
        {
            _lstFiles.Items.Clear();
        }

        public void Add()
        {
            Microsoft.Win32.OpenFileDialog fd = new Microsoft.Win32.OpenFileDialog();
            fd.Multiselect = true;
            if (string.IsNullOrEmpty(Settings.Default.FileDialogInitialLocation) == false)
            {
                fd.InitialDirectory = Settings.Default.FileDialogInitialLocation;
            }
            if (fd.ShowDialog() == true)
            {
                Add(fd.FileNames);

                if (fd.FileNames.Length > 0)
                {
                    Settings.Default.FileDialogInitialLocation = Path.GetDirectoryName(fd.FileNames[0]);
                    Settings.Default.Save();
                }
            }
        }

        public void Add(string[] files)
        {
            foreach (string file in files)
            {
                if (_lstFiles.Items.Contains(file) == false)
                {
                    _lstFiles.Items.Add(file);
                }
            }
        }

        public void Remove()
        {
            System.Collections.IList filesToRemove = new List<string>();
            foreach (string file in _lstFiles.SelectedItems)
            {
                filesToRemove.Add(file);
            }

            foreach (string fileToRemove in filesToRemove)
            {
                _lstFiles.Items.Remove(fileToRemove);
            }
        }

        public void CreatePatch(string patchName)
        {
            string patchRoot = Path.Combine(CreatedPatchesDir, patchName);

            RemovePreviousPatch(patchName, patchRoot);
            CreatePatchDirectoryStructure(patchRoot);
            CreateDetailsFile(patchRoot);
            CreateContentsFile(patchRoot);

            AddPatchContents(patchRoot, _lstFiles);

            CompressPatchFile(patchName, patchRoot);
            RemovePatchDirectory(patchRoot);
            string result = Publish(patchName);
            System.Windows.MessageBox.Show(string.Format("Patch Created at {0}", result));
        }

        private void CompressPatchFile(string patchName, string patchRoot)
        {
            System.IO.Compression.ZipFile.CreateFromDirectory(patchRoot, Path.Combine(CreatedPatchesDir, patchName + Settings.Default.DefaultFileExtension));
        }

        private void AddPatchContents(string patchRoot, ListBox lstFiles)
        {
            foreach (string fileToZip in lstFiles.Items)
            {
                File.Copy(fileToZip, Path.Combine(Path.Combine(patchRoot, "patch"), Path.GetFileName(fileToZip)));
                File.AppendAllText(Path.Combine(patchRoot, "contents.txt"), fileToZip + Environment.NewLine);
            }
        }

        private static void CreateContentsFile(string patchRoot)
        {
            File.Create(Path.Combine(patchRoot, "contents.txt")).Close();
        }

        private static void CreateDetailsFile(string patchRoot)
        {
            File.Create(Path.Combine(patchRoot, "details.txt")).Close();
            File.AppendAllText(Path.Combine(patchRoot, "details.txt"), string.Format("Patch created at {0} by {1} on {2}", DateTime.Now, Environment.UserName, Environment.MachineName));
        }

        private static void CreatePatchDirectoryStructure(string patchRoot)
        {
            Directory.CreateDirectory(patchRoot);
            Directory.CreateDirectory(Path.Combine(patchRoot, "patch"));
            Directory.CreateDirectory(Path.Combine(patchRoot, "original"));
        }

        private void RemovePreviousPatch(string patchName, string patchRoot)
        {
            File.Delete(Path.Combine(CreatedPatchesDir, patchName + Settings.Default.DefaultFileExtension));
            RemovePatchDirectory(patchRoot);
        }

        private static void RemovePatchDirectory(string patchRoot)
        {
            Helpers.ForceDeleteDirectory(patchRoot);
        }

        private string Publish(string patchName)
        {
            string target = Path.Combine(Settings.Default.PatchRepository, patchName + Settings.Default.DefaultFileExtension);
            File.Copy(Path.Combine(CreatedPatchesDir, patchName + Settings.Default.DefaultFileExtension), target, true);
            return target;
        }
    }
}

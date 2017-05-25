using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System;
using System.Xml.Serialization;

namespace tap
{
    class Manage
    {
        private string AppliedPatchesDir = "AppliedPatches";
        private System.Windows.Controls.ListBox _lstAvailable;
        private System.Windows.Controls.ListBox _lstApplied;
        private string _fdPath;

        public Manage(System.Windows.Controls.ListBox lstAvailable, System.Windows.Controls.ListBox lstApplied)
        {
            _lstAvailable = lstAvailable;
            _lstApplied = lstApplied;
            if (!Directory.Exists(AppliedPatchesDir))
            {
                Directory.CreateDirectory(AppliedPatchesDir);
            }
            Refresh();
        }

        private void LoadPatches()
        {
            _lstAvailable.Items.Clear();
            // load patch names from known network location
            List<string> files = Directory.EnumerateFiles(Settings.Default.PatchRepository).ToList();
            foreach (string available in files)
            {
                string patchName = Path.GetFileNameWithoutExtension(available);
                if (!_lstApplied.Items.Contains(patchName))
                {
                    _lstAvailable.Items.Add(Path.GetFileNameWithoutExtension(available));
                }
            }
        }

        private void LoadAppliedPatches()
        {
            _lstApplied.Items.Clear();
            List<string> appliedPatches = Directory.EnumerateDirectories(Path.Combine(AppliedPatchesDir)).ToList();
            // ok, we have a list of applied patches, now we need to examine their patchrecord documents and sort by created date
            SortedDictionary<DateTime, string> sortedAppliedPatches = new SortedDictionary<DateTime, string>();
            foreach (string applied in appliedPatches)
            {
                string patchRecordFile = Path.Combine(applied, "patchrecord.xml");
                if (File.Exists(patchRecordFile))
                {
                    DateTime creation = File.GetCreationTime(patchRecordFile);
                    sortedAppliedPatches.Add(creation, applied);
                }
            }

            foreach (KeyValuePair<DateTime, string> applied in sortedAppliedPatches)
            {
                _lstApplied.Items.Add(applied.Value.Substring(applied.Value.LastIndexOf("\\") + 1));
            }
        }

        public void ApplyPatch()
        {
            if (_lstAvailable.SelectedItem != null)
            {
                string patchName = GetPatchName();
                string patchRecordFile = Path.Combine(AppliedPatchesDir, patchName, "patchrecord.xml");

                if (Directory.Exists(Path.Combine(AppliedPatchesDir, patchName)) && File.Exists(patchRecordFile))
                {
                    System.Windows.MessageBox.Show(string.Format("Patch {0} is already applied, please remove it first", patchName));
                    return;
                }

                string targetDir = SelectTargetDirectory();
                if (targetDir != null)
                {
                    
                    string patchRoot = Path.Combine(AppliedPatchesDir, patchName);
                    RemovePreviousPatch(patchRoot);
                    CreatePatchRoot(patchRoot);
                    string patchFile = DownloadPatchFile(patchName, patchRoot);
                    ExtractPatchFile(patchRoot, patchFile);
                    RemovePatchFile(patchFile);
                    // by this point we have a fully extracted patch file ready for deployment
                    if(DeployPatch(patchRoot, targetDir))
                    {
                        MessageBox.Show(string.Format("Patch {0} Deployed", patchName));
                        Refresh();
                    }
                    else
                    {
                        RemovePreviousPatch(patchRoot);
                    }
                }
            }
        }

        internal void RevertPatch()
        {
            if (_lstApplied.SelectedItem != null)
            {
                string patchName = GetAppliedPatchName();
                string patchRoot = Path.Combine(AppliedPatchesDir, patchName);
                Dictionary<string, string> patchMap = DeserializePatchMap(patchRoot);
                RevertPatch(patchRoot, patchMap);
                Helpers.ForceDeleteDirectory(patchRoot);
                MessageBox.Show(string.Format("Patch {0} Reverted", patchName));
                Refresh();
            }
        }

        private void RevertPatch(string patchRoot, Dictionary<string, string> patchMap)
        {
            foreach(KeyValuePair<string, string> patchItem in patchMap)
            {
                string source = Path.Combine(patchRoot, "original", patchItem.Key);
                string target = patchItem.Value;
                if (Helpers.IsDirectory(target))
                {
                    File.Delete(target);
                }
                else
                {
                    File.Copy(source, target, true);
                }
            }
        }

        private void RemoveAppliedPatchFiles(Dictionary<string, string> patchMap)
        {
            foreach(KeyValuePair<string, string> appliedPatch in patchMap)
            {
                string fileToRemove = appliedPatch.Value;
                if (Helpers.IsDirectory(fileToRemove))
                {
                    fileToRemove = Path.Combine(appliedPatch.Value, appliedPatch.Key);
                }

                if (File.Exists(fileToRemove))
                {
                    File.Delete(fileToRemove);
                }
            }
        }

        private string SelectTargetDirectory()
        {
            FolderBrowserDialog dialog = new FolderBrowserDialog();
            dialog.Description = "Select target folder";
            if (_fdPath != null && Directory.Exists(_fdPath))
            {
                dialog.SelectedPath = _fdPath;
            }
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                _fdPath = dialog.SelectedPath;
                return dialog.SelectedPath;
            }
            return null;
        }

        private bool DeployPatch(string patchRoot, string targetDir)
        {
            Dictionary<string, string> patchMap = new Dictionary<string, string>();
            List<string> files = Directory.EnumerateFiles(Path.Combine(patchRoot, "patch")).ToList();
            foreach (string patchFile in files)
            {
                string target = FindPatchFileInDir(Path.GetFileName(patchFile), targetDir);
                if (target == null) return false;
                patchMap.Add(Path.GetFileName(patchFile), target);
            }
            TakeOriginalCopies(patchRoot, patchMap);
            DeployReplacementFiles(patchRoot, patchMap);
            SerializePatchMap(patchRoot, patchMap);
            return true;
        }

        private void SerializePatchMap(string patchRoot, Dictionary<string, string> patchMap)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(PatchItem[]),
                                 new XmlRootAttribute() { ElementName = "PatchItems" });
            TextWriter writer = new StreamWriter(Path.Combine(patchRoot, "patchrecord.xml"));

            serializer.Serialize(writer,
              patchMap.Select(kv => new PatchItem() { PatchFile = kv.Key, PatchFileTarget = kv.Value }).ToArray());

            writer.Close();
        }

        private Dictionary<string, string> DeserializePatchMap(string patchRoot)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(PatchItem[]),
                     new XmlRootAttribute() { ElementName = "PatchItems" });
            TextReader reader = new StreamReader(Path.Combine(patchRoot, "patchrecord.xml"));
            Dictionary<string, string> patchMap = ((PatchItem[])serializer.Deserialize(reader))
               .ToDictionary(i => i.PatchFile, i => i.PatchFileTarget);
            reader.Close();
            return patchMap;
        }

        private void DeployReplacementFiles(string patchRoot, Dictionary<string, string> patchMap)
        {
            foreach(KeyValuePair<string, string> patchItem in patchMap)
            {
                string source = Path.Combine(patchRoot, "patch", patchItem.Key);
                string target = patchItem.Value;
                if (Helpers.IsDirectory(target))
                {
                    target = Path.Combine(target, patchItem.Key);
                }
                File.Copy(source, target, true);
            }
        }

        private void TakeOriginalCopies(string patchRoot, Dictionary<string, string> patchMap)
        {
            foreach(KeyValuePair<string, string> patchItem in patchMap)
            {
                if (Helpers.IsDirectory(patchItem.Value))
                {
                    // if the deployment target is a directory and not a file, we don't need to take a back up
                    continue;
                }
                File.Copy(patchItem.Value, Path.Combine(patchRoot, "original", patchItem.Key));
            }
        }

        private string FindPatchFileInDir(string patchFile, string targetDir)
        {
            string target;
            string[] files = Directory.GetFiles(targetDir, patchFile, SearchOption.AllDirectories);
            if(files.Length == 0)
            {
                MessageBox.Show(string.Format("Cannot find existing file {0}, please select target folder", patchFile));
                target = SelectTargetDirectory();
            }
            else if (files.Length > 1)
            {
                MessageBox.Show(string.Format("Found more than one occurence of file {0}, please select exact target folder", patchFile));
                target = SelectTargetDirectory();
            }
            else
            {
                target = files[0];
            }
            return target;
        }

        private static void ExtractPatchFile(string patchRoot, string patchFile)
        {
            System.IO.Compression.ZipFile.ExtractToDirectory(patchFile, patchRoot);
        }

        private void RemovePatchFile(string patchFile)
        {
            File.Delete(patchFile);
        }

        private void CreatePatchRoot(string patchRoot)
        {
            Directory.CreateDirectory(patchRoot);
        }

        private static string DownloadPatchFile(string patchName, string patchRoot)
        {
            string patchTarget = Path.Combine(patchRoot, patchName + Settings.Default.DefaultFileExtension);
            File.Copy(Path.Combine(Settings.Default.PatchRepository, patchName + Settings.Default.DefaultFileExtension), patchTarget, true);
            return patchTarget;
        }

        private static void RemovePreviousPatch(string patchRoot)
        {
            Helpers.ForceDeleteDirectory(patchRoot);
        }

        private string GetPatchName()
        {
            return _lstAvailable.SelectedItem.ToString();
        }

        private string GetAppliedPatchName()
        {
            return _lstApplied.SelectedItem.ToString();
        }

        public void Refresh()
        {
            LoadAppliedPatches();
            LoadPatches();
        }
    }
}

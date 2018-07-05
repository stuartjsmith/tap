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
            if (string.IsNullOrEmpty(Settings.Default.PatchRepository)) return;
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
                    System.Windows.MessageBox.Show(string.Format("Patch {0} is already applied, please remove it first", patchName), "The bitter taste of disappointment", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
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
                        MessageBox.Show(string.Format("Patch {0} Deployed", patchName), "The sweet smell of success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        Refresh();
                    }
                    else
                    {
                        RemovePreviousPatch(patchRoot);
                    }
                }
            }
        }

        internal void Delete()
        {
            if (_lstApplied.SelectedItem != null)
            {
                string patchName = GetAppliedPatchName();
                string patchRoot = Path.Combine(AppliedPatchesDir, patchName);
                Helpers.ForceDeleteDirectory(patchRoot);
                MessageBox.Show(string.Format("Patch {0} Deleted", patchName), "The sweet smell of success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Refresh();
            }
        }

        internal void RevertPatch()
        {
            if (_lstApplied.SelectedItem != null)
            {
                string patchName = GetAppliedPatchName();
                string patchRoot = Path.Combine(AppliedPatchesDir, patchName);
                Dictionary<string, string> patchMap = DeserializePatchMap(patchRoot);
                List<string> overLaps = GetOverlappingPatches(patchRoot, patchMap, false);
                if(overLaps.Count > 0)
                {
                    string message = "Patches containing the same files have been found, you must remove the patches in this order before removing this one: ";
                    for(int idx=0; idx < overLaps.Count; idx++)
                    {
                        if(idx!=0)
                        {
                            message = message + ", ";
                        }
                        message = message + overLaps[idx];
                    }
                    MessageBox.Show(message, "I told you this when you put it on!", MessageBoxButtons.OK, MessageBoxIcon.Stop);
                    return;
                }
                bool success = RevertPatch(patchRoot, patchMap);
                if (success)
                {
                    Helpers.ForceDeleteDirectory(patchRoot);
                    MessageBox.Show(string.Format("Patch {0} Reverted", patchName), "The sweet smell of success", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                Refresh();
            }
        }

        private List<string> GetOverlappingPatches(string patchRoot, Dictionary<string, string> patchMap, bool applyPatch)
        {
            bool gotPatchStart = false;
            List<string> overlappingPatches = new List<string>();
            // this method needs to make sure that no other previously applied patches overlap in terms of file content with the one we are trying to remove
            foreach(string appliedPatch in _lstApplied.Items)
            {
                string otherPatchRoot = Path.Combine(AppliedPatchesDir, appliedPatch);
                if (!applyPatch)
                {
                    if (gotPatchStart == false && !patchRoot.Equals(otherPatchRoot))
                    {
                        // we don't care about patches applied prioer to this one
                        continue;
                    }
                    if (patchRoot.Equals(otherPatchRoot))
                    {
                        gotPatchStart = true;
                        continue;
                    }
                }
                Dictionary<string, string> otherPatchMap = DeserializePatchMap(otherPatchRoot);
                if(DoPatchMapsContainSameTargets(patchMap, otherPatchMap))
                {
                    overlappingPatches.Insert(0, appliedPatch);
                    break;
                }
            }

            return overlappingPatches;
        }

        private bool DoPatchMapsContainSameTargets(Dictionary<string, string> patchMap1, Dictionary<string, string> patchMap2)
        {
            Dictionary<string, string> patchMap1_qualified = GetQualifiedPatchMap(patchMap1);
            Dictionary<string, string> patchMap2_qualified = GetQualifiedPatchMap(patchMap2);

            foreach (string target in patchMap1_qualified.Values)
            {
                if(patchMap2_qualified.ContainsValue(target))
                {
                    return true;
                }
            }
            return false;
        }

        private Dictionary<string, string> GetQualifiedPatchMap(Dictionary<string, string> patchMap)
        {
            Dictionary<string, string> qualifiedPatchMap = new Dictionary<string, string>();
            foreach(KeyValuePair<string, string> patchItem in patchMap)
            {
                string key = patchItem.Key;
                string value = patchItem.Value;
                if(Helpers.IsDirectory(value))
                {
                    value = Path.Combine(value, key);
                }
                qualifiedPatchMap.Add(key, value);
            }
            return qualifiedPatchMap;
        }

        private bool RevertPatch(string patchRoot, Dictionary<string, string> patchMap, bool showErrors = true)
        {
            bool success = true;
            foreach (KeyValuePair<string, string> patchItem in patchMap)
            {
                string source = Path.Combine(patchRoot, "original", patchItem.Key);
                string target = patchItem.Value;
                if (Helpers.IsDirectory(target))
                {
                    try
                    {
                        File.Delete(Path.Combine(target, patchItem.Key));
                    }
                    catch(Exception e)
                    {
                        if (showErrors)
                        {
                            MessageBox.Show("Unable to revert patch - " + e.Message);
                        }
                        success = false;
                    }
                }
                else
                {
                    try
                    {
                        File.Copy(source, target, true);
                    }
                    catch (IOException e)
                    {
                        if (showErrors)
                        {
                            MessageBox.Show("Unable to revert patch - " + e.Message);
                        }
                        success = false;
                    }
                }
            }
            return success;
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
            if (Settings.Default.FolderDialogInitialLocation != null && Directory.Exists(Settings.Default.FolderDialogInitialLocation))
            {
                dialog.SelectedPath = Settings.Default.FolderDialogInitialLocation;
            }
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                Settings.Default.FolderDialogInitialLocation = dialog.SelectedPath;
                Settings.Default.Save();
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
            List<string> overLaps = GetOverlappingPatches(patchRoot, patchMap, true);
            if(overLaps.Count > 0)
            {
                string message = "The following patches contain some of the same files, this is ok, but be aware that these patches will be overwritten by this one. When you remove the patches, they must be removed in the correct order to ensure integrity: ";
                for (int idx = 0; idx < overLaps.Count; idx++)
                {
                    if (idx != 0)
                    {
                        message = message + ", ";
                    }
                    message = message + overLaps[idx];
                }
                if (DialogResult.Cancel == MessageBox.Show(message, "Overlapping patches", MessageBoxButtons.OKCancel, MessageBoxIcon.Information))
                {
                    return false;
                }
            }
            TakeOriginalCopies(patchRoot, patchMap);
            bool success = DeployReplacementFiles(patchRoot, patchMap);
            if (success == false) RevertPatch(patchRoot, patchMap, false);
            SerializePatchMap(patchRoot, patchMap);
            return success;
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

        private bool DeployReplacementFiles(string patchRoot, Dictionary<string, string> patchMap)
        {
            bool result = true;
            foreach (KeyValuePair<string, string> patchItem in patchMap)
            {
                string source = Path.Combine(patchRoot, "patch", patchItem.Key);
                string target = patchItem.Value;
                if (Helpers.IsDirectory(target))
                {
                    target = Path.Combine(target, patchItem.Key);
                }
                try
                {
                    File.Copy(source, target, true);
                }
                catch(IOException e)
                {
                    MessageBox.Show("Unable to deploy patch - " + e.Message);
                    result = false;
                }
            }
            return result;
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
                MessageBox.Show(string.Format("Cannot find existing file {0}, please select target folder", patchFile), "File doesn't yet exist", MessageBoxButtons.OK, MessageBoxIcon.Information);
                target = SelectTargetDirectory();
            }
            else if (files.Length > 1)
            {
                MessageBox.Show(string.Format("Found more than one occurence of file {0}, please select exact target folder", patchFile), "I found a few of these", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

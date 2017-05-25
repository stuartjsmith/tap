using System.IO;

namespace tap
{
    class Helpers
    {
        public static void ForceDeleteDirectory(string path)
        {
            if (!Directory.Exists(path)) return;

            var directory = new DirectoryInfo(path) { Attributes = FileAttributes.Normal };

            foreach (var info in directory.GetFileSystemInfos("*", SearchOption.AllDirectories))
            {
                info.Attributes = FileAttributes.Normal;
            }

            directory.Delete(true);
        }

        public static bool IsDirectory(string path)
        {
            try
            {
                FileAttributes attr = File.GetAttributes(path);
                return ((attr & FileAttributes.Directory) == FileAttributes.Directory);
            }
            catch(IOException)
            {
                return false;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace tap
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Create _create;
        private Create Create
        {
            get
            {
                if(_create == null)
                {
                    _create = new Create(lstFiles);
                }
                return _create;
            }
        }

        private Manage _manage;
        private Manage Manage
        {
            get
            {
                if (_manage == null)
                {
                    _manage = new Manage(lstAvailable, lstApplied);
                }
                return _manage;
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            txtPatchRepository.Text = Settings.Default.PatchRepository;
            txtPatchRepository.TextChanged += txtPatchRepository_TextChanged;
            txtPatchRepository.LostFocus += txtPatchRepository_LostFocus;
        }

        private void txtPatchRepository_LostFocus(object sender, RoutedEventArgs e)
        {
            Manage.Refresh();
        }

        private void btnAddFile_Click(object sender, RoutedEventArgs e)
        {
            Create.Add();
        }

        private void btnRemoveFile_Click(object sender, RoutedEventArgs e)
        {
            Create.Remove();
        }

        private void btnCreate_Click(object sender, RoutedEventArgs e)
        {
            Create.CreatePatch(GetPatchName());
        }

        private void btnClear_Click(object sender, RoutedEventArgs e)
        {
            Create.Clear();
        }

        private void btnApply_Click(object sender, RoutedEventArgs e)
        {
            Manage.ApplyPatch();
        }

        private void btnRefresh_Click(object sender, RoutedEventArgs e)
        {
            Manage.Refresh();
        }

        private void btnRevert_Click(object sender, RoutedEventArgs e)
        {
            Manage.RevertPatch();
        }

        private void lstFiles_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                // Note that you can have more than one file.
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);

                Create.Add(files);
            }
        }

        private string GetPatchName()
        {
            if (string.IsNullOrEmpty(txtPatchName.Text))
            {
                txtPatchName.Text = Environment.UserName + "_" + Guid.NewGuid();
            }

            string patchName = txtPatchName.Text;
            return patchName;
        }

        private void TabItem_Loaded(object sender, RoutedEventArgs e)
        {
            Manage.Refresh();
        }

        private void txtPatchRepository_TextChanged(object sender, TextChangedEventArgs e)
        {
            Settings.Default.PatchRepository = txtPatchRepository.Text;
            Settings.Default.Save();
        }

        private void lstFiles_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effects = DragDropEffects.All;
            else
                e.Effects = DragDropEffects.None;
        }
    }
}

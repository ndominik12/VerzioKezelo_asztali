using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using System.IO;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace VerzioKezelo
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private CommitManager _mgr = new CommitManager();
        public MainWindow()
        {
            InitializeComponent();
            RefreshCommits();
        }

        private T GetControl<T>(string name) where T: class
        {
            return this.FindName(name) as T;
        }

        private void Log(string s)
        {
            var txt = GetControl<TextBox>("TxtLog");
            if (txt == null) return;
            txt.AppendText($"{DateTime.Now:HH:mm:ss} - {s}\n");
            txt.ScrollToEnd();
        }

        private void BtnSelectFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog();
            dlg.CheckFileExists = false;
            dlg.CheckPathExists = true;
            dlg.FileName = "Válassz mappát (kattints egy fájlra a mappában)";
            var res = dlg.ShowDialog();
            if (res == true && !string.IsNullOrEmpty(dlg.FileName))
            {
                var folder = System.IO.Path.GetDirectoryName(dlg.FileName);
                _mgr.SetRoot(folder);
                var txt = GetControl<TextBlock>("TxtFolder");
                if (txt != null) txt.Text = folder;
                Log("Mappa kiválasztva: " + folder);
                RefreshCommits();
            }
        }

        private void BtnCommit_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_mgr.Root)) { MessageBox.Show("Előbb válassz mappát!"); return; }
            var tb = GetControl<TextBox>("TxtCommitName");
            var name = (tb?.Text ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(name)) { MessageBox.Show("Adj nevet a commitnak!"); return; }
            try
            {
                var created = _mgr.CreateCommit(name);
                Log("Commit létrehozva: " + created);
                RefreshCommits();
            }
            catch (Exception ex)
            {
                Log("Hiba commit közben: " + ex.Message);
                MessageBox.Show("Hiba: " + ex.Message);
            }
        }

        private void RefreshCommits()
        {
            var combo = GetControl<ComboBox>("ComboCommits");
            if (combo == null) return;
            combo.Items.Clear();
            if (string.IsNullOrEmpty(_mgr.Root)) return;
            foreach (var c in _mgr.GetCommits()) combo.Items.Add(c);
            if (combo.Items.Count > 0) combo.SelectedIndex = 0;
        }

        private void BtnRestore_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_mgr.Root)) { MessageBox.Show("Előbb válassz mappát!"); return; }
            var combo = GetControl<ComboBox>("ComboCommits");
            if (combo == null || combo.SelectedItem == null) { MessageBox.Show("Válassz commitot a listából!"); return; }
            var sel = combo.SelectedItem.ToString();
            try
            {
                _mgr.RestoreCommit(sel);
                Log("Visszaállítva: " + sel);
            }
            catch (Exception ex)
            {
                Log("Hiba visszaállításkor: " + ex.Message);
                MessageBox.Show("Hiba: " + ex.Message);
            }
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_mgr.Root)) { MessageBox.Show("Előbb válassz mappát!"); return; }
            var combo2 = GetControl<ComboBox>("ComboCommits");
            if (combo2 == null || combo2.SelectedItem == null) { MessageBox.Show("Válassz commitot a listából!"); return; }
            var sel = combo2.SelectedItem.ToString();
            if (MessageBox.Show($"Törölni akarod a '{sel}' commitot?","Figyelem", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            try
            {
                _mgr.DeleteCommit(sel);
                Log("Törölve: " + sel);
                RefreshCommits();
            }
            catch (Exception ex)
            {
                Log("Hiba törléskor: " + ex.Message);
                MessageBox.Show("Hiba: " + ex.Message);
            }
        }

        private void BtnNeedCommit_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_mgr.Root)) { MessageBox.Show("Előbb válassz mappát!"); return; }
            bool needed = _mgr.IsCommitNeeded();
            MessageBox.Show(needed ? "Szükséges új commit." : "Nem szükséges commit.");
            Log(needed ? "Commit szükséges." : "Nincs változás.");
        }
    }
}

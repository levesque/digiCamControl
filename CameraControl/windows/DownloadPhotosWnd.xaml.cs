﻿#region Licence

// Distributed under MIT License
// ===========================================================
// 
// digiCamControl - DSLR camera remote control open source software
// Copyright (C) 2014 Duka Istvan
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, 
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF 
// MERCHANTABILITY,FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. 
// IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY 
// CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT,
// TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH 
// THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

#endregion

using CameraControl.Core.Scripting.ScriptCommands;

#region

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Data;
using System.Windows.Forms;
using CameraControl.Core;
using CameraControl.Core.Classes;
using CameraControl.Core.Interfaces;
using CameraControl.Core.Translation;
using CameraControl.Core.Wpf;
using CameraControl.Devices;
using CameraControl.Devices.Classes;
using FileInfo = System.IO.FileInfo;
using MessageBox = System.Windows.Forms.MessageBox;

#endregion

//using CameraControl.Classes;
//using CameraControl.Translation;
//using HelpProvider = CameraControl.Classes.HelpProvider;

namespace CameraControl.windows
{
    /// <summary>
    /// Interaction logic for DownloadPhotosWnd.xaml
    /// </summary>
    public partial class DownloadPhotosWnd : INotifyPropertyChanged, IWindow
    {
        private bool delete;
        private bool format;
        private ProgressWindow dlg = new ProgressWindow();
        private Dictionary<ICameraDevice, int> _timeDif = new Dictionary<ICameraDevice, int>();

        private Dictionary<ICameraDevice, AsyncObservableCollection<FileItem>> _itembycamera =
            new Dictionary<ICameraDevice, AsyncObservableCollection<FileItem>>();

        private List<DateTime> _timeTable = new List<DateTime>();

        public ObservableCollection<DownloadableItems> Groups { get; set; }

        private ICameraDevice _cameraDevice;

        public ICameraDevice CameraDevice
        {
            get { return _cameraDevice; }
            set
            {
                _cameraDevice = value;
                NotifyPropertyChanged("CameraDevice");
            }
        }

        private AsyncObservableCollection<FileItem> _items;

        public AsyncObservableCollection<FileItem> Items
        {
            get { return _items; }
            set
            {
                _items = value;
                NotifyPropertyChanged("Items");
            }
        }

        public RelayCommand<string> SelectAllCommand { get; set; }
        public RelayCommand<string> SelectNoneCommand { get; set; }
        public RelayCommand<string> SelectInvertCommand { get; set; }

        public DownloadPhotosWnd()
        {
            Groups = new ObservableCollection<DownloadableItems>();
            Items = new AsyncObservableCollection<FileItem>();
            SelectAllCommand = new RelayCommand<string>(SelectAll);
            SelectNoneCommand = new RelayCommand<string>(SelectNone);
            SelectInvertCommand = new RelayCommand<string>(SelectInvert);
            InitializeComponent();
        }

        private void SelectAll(string serial)
        {
            foreach (FileItem fileItem in Items)
            {
                if (fileItem.Device.SerialNumber == serial)
                    fileItem.IsChecked = true;
            }
        }

        private void SelectNone(string serial)
        {
            foreach (FileItem fileItem in Items)
            {
                if (fileItem.Device.SerialNumber == serial)
                    fileItem.IsChecked = false;
            }
        }

        private void SelectInvert(string serial)
        {
            foreach (FileItem fileItem in Items)
            {
                if (fileItem.Device.SerialNumber == serial)
                    fileItem.IsChecked = !fileItem.IsChecked;
            }
        }

        private void btn_help_Click(object sender, RoutedEventArgs e)
        {
            //HelpProvider.Run(HelpSections.DownloadPhotos);
        }

        #region Implementation of INotifyPropertyChanged

        public virtual event PropertyChangedEventHandler PropertyChanged;

        public virtual void NotifyPropertyChanged(String info)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(info));
            }
        }

        #endregion

        #region Implementation of IWindow

        public void ExecuteCommand(string cmd, object param)
        {
            switch (cmd)
            {
                case WindowsCmdConsts.DownloadPhotosWnd_Show:
                    Dispatcher.Invoke(new Action(delegate
                                                     {
                                                         if (dlg.IsVisible)
                                                             return;
                                                         CameraDevice = param as ICameraDevice;
                                                         Title = TranslationStrings.DownloadWindowTitle + "-" +
                                                                 ServiceProvider.Settings.CameraProperties.Get(
                                                                     CameraDevice).
                                                                     DeviceName;
                                                         if (param == null)
                                                             return;
                                                         Show();
                                                         Activate();
                                                         Topmost = true;
                                                         Topmost = false;
                                                         Focus();
                                                         dlg.Show();
                                                         Items.Clear();
                                                         Thread thread = new Thread(PopulateImageList);
                                                         thread.Start();
                                                     }));
                    break;
                case WindowsCmdConsts.DownloadPhotosWnd_Hide:
                    Hide();
                    break;
                case CmdConsts.All_Close:
                    Dispatcher.Invoke(new Action(delegate
                                                     {
                                                         Hide();
                                                         Close();
                                                     }));
                    break;
            }
        }

        #endregion

        private void MetroWindow_Closing(object sender, CancelEventArgs e)
        {
            if (IsVisible)
            {
                e.Cancel = true;
                ServiceProvider.WindowsManager.ExecuteCommand(WindowsCmdConsts.DownloadPhotosWnd_Hide);
            }
        }

        private void PopulateImageList()
        {
            _timeDif.Clear();
            _itembycamera.Clear();
            Items.Clear();
            _timeTable.Clear();
            if (ServiceProvider.DeviceManager.ConnectedDevices.Count == 0)
                return;
            //int threshold = 0;
            //bool checkset = false;

            foreach (ICameraDevice cameraDevice in ServiceProvider.DeviceManager.ConnectedDevices)
            {
                CameraProperty property = cameraDevice.LoadProperties();
                cameraDevice.DisplayName = property.DeviceName;
                dlg.Label = cameraDevice.DisplayName;
                try
                {
                    var images = cameraDevice.GetObjects(null);
                    if (images.Count > 0)
                    {
                        foreach (DeviceObject deviceObject in images)
                        {
                            if (!_itembycamera.ContainsKey(cameraDevice))
                                _itembycamera.Add(cameraDevice, new AsyncObservableCollection<FileItem>());
                            var fileitem = new FileItem(deviceObject, cameraDevice);

                            PhotoSession session = (PhotoSession)cameraDevice.AttachedPhotoSession ??
                                       ServiceProvider.Settings.DefaultSession;
                            // check if file exist with same name from this camera
                            fileitem.IsChecked = session.GetFile(deviceObject.FileName, cameraDevice.SerialNumber) ==
                                                 null;

                            _itembycamera[cameraDevice].Add(fileitem);
                            //Items.Add(new FileItem(deviceObject, cameraDevice));
                        }
                    }
                }
                catch (Exception exception)
                {
                    StaticHelper.Instance.SystemMessage = TranslationStrings.LabelErrorLoadingFileList;
                    Log.Error("Error loading file list", exception);
                }
                Thread.Sleep(500);
            }

            Dispatcher.Invoke(new Action(delegate
            {
                foreach (var fileItem in _itembycamera.Values.SelectMany(lists => lists))
                {
                    Items.Add(fileItem);
                }
                CollectionView myView;
                myView = (CollectionView) CollectionViewSource.GetDefaultView(Items);
                myView.GroupDescriptions.Clear();

                if (myView.CanGroup == true)
                {
                    PropertyGroupDescription groupDescription
                        = new PropertyGroupDescription("Device");
                    myView.GroupDescriptions.Add(groupDescription);
                }

                if (ServiceProvider.DeviceManager.ConnectedDevices.Count > 1)
                {
                    lst_items.Visibility = Visibility.Visible;
                    lst_items_simple.Visibility = Visibility.Collapsed;
                    lst_items.ItemsSource = myView;
                }
                else
                {
                    lst_items.Visibility = Visibility.Collapsed;
                    lst_items_simple.Visibility = Visibility.Visible;
                    lst_items_simple.ItemsSource = Items;
                }
            }));
            dlg.Hide();
        }

        private void btn_download_Click(object sender, RoutedEventArgs e)
        {
            if ((chk_delete.IsChecked == true || chk_format.IsChecked == true) &&
                MessageBox.Show(TranslationStrings.LabelAskForDelete, "", MessageBoxButtons.YesNo) !=
                System.Windows.Forms.DialogResult.Yes)
                return;
            dlg.Show();
            delete = chk_delete.IsChecked == true;
            format = chk_format.IsChecked == true;
            ServiceProvider.WindowsManager.ExecuteCommand(WindowsCmdConsts.DownloadPhotosWnd_Hide);
            Thread thread = new Thread(TransferFiles);
            thread.Start();
        }

        private void TransferFiles()
        {
            DateTime starttime = DateTime.Now;
            long totalbytes = 0;
            bool somethingwrong = false;
            AsyncObservableCollection<FileItem> itemstoExport =
                new AsyncObservableCollection<FileItem>(Items.Where(x => x.IsChecked));
            dlg.MaxValue = itemstoExport.Count;
            dlg.Progress = 0;
            int i = 0;
            foreach (FileItem fileItem in itemstoExport)
            {
                if (fileItem.ItemType == FileItemType.Missing)
                    continue;
                if (!fileItem.IsChecked)
                    continue;
                dlg.Label = fileItem.FileName;
                dlg.ImageSource = fileItem.Thumbnail;
                PhotoSession session = (PhotoSession) fileItem.Device.AttachedPhotoSession ??
                                       ServiceProvider.Settings.DefaultSession;
                string fileName = "";

                if (!session.UseOriginalFilename)
                {
                    fileName =
                        session.GetNextFileName(Path.GetExtension(fileItem.FileName),
                                                fileItem.Device);
                }
                else
                {
                    fileName = Path.Combine(session.Folder, fileItem.FileName);
                    if (File.Exists(fileName))
                        fileName =
                            StaticHelper.GetUniqueFilename(
                                Path.GetDirectoryName(fileName) + "\\" + Path.GetFileNameWithoutExtension(fileName) +
                                "_", 0,
                                Path.GetExtension(fileName));
                }

                string dir = Path.GetDirectoryName(fileName);
                if (dir != null && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                try
                {
                    fileItem.Device.TransferFile(fileItem.DeviceObject.Handle, fileName);
                }
                catch (Exception exception)
                {
                    somethingwrong = true;
                    Log.Error("Transfer error", exception);
                }

                // double check if file was transferred
                if (File.Exists(fileName))
                {
                    if (delete)
                        fileItem.Device.DeleteObject(fileItem.DeviceObject);
                }
                else
                {
                    somethingwrong = true;
                }
                if (!File.Exists(fileName))
                {
                    MessageBox.Show("Unable download file. Aborting!");
                    break;
                }
                totalbytes += new FileInfo(fileName).Length;
                var item = session.AddFile(fileName);
                item.CameraSerial = fileItem.Device.SerialNumber;
                item.OriginalName = fileItem.FileName;
                i++;
                dlg.Progress = i;
            }

            Log.Debug("File transfer done");

            if (format)
            {
                if (!somethingwrong)
                {
                    foreach (ICameraDevice connectedDevice in ServiceProvider.DeviceManager.ConnectedDevices)
                    {
                        try
                        {
                            Log.Debug("Start format");
                            connectedDevice.FormatStorage(null);
                            Thread.Sleep(200);
                            Log.Debug("Format done");
                        }
                        catch (Exception exception)
                        {
                            Log.Error("Unable to format device ", exception);
                        }
                    }
                }
                else
                {
                    Log.Debug("File transfer failed, format aborted!");
                    StaticHelper.Instance.SystemMessage = "File transfer failed, format aborted!";
                }
            }
            dlg.Hide();
            double transfersec = (DateTime.Now - starttime).TotalSeconds;
            Log.Debug(string.Format("[BENCHMARK]Total byte transferred ;{0} Total seconds :{1} Speed : {2} Mbyte/sec ",
                                    totalbytes,
                                    transfersec, (totalbytes/transfersec/1024/1024).ToString("0000.00")));
            ServiceProvider.Settings.Save();
        }

        private void btn_all_Click(object sender, RoutedEventArgs e)
        {
            foreach (FileItem fileItem in Items)
            {
                fileItem.IsChecked = true;
            }
        }

        private void btn_none_Click(object sender, RoutedEventArgs e)
        {
            foreach (FileItem fileItem in Items)
            {
                fileItem.IsChecked = false;
            }
        }

        private void btn_invert_Click(object sender, RoutedEventArgs e)
        {
            foreach (FileItem fileItem in Items)
            {
                fileItem.IsChecked = !fileItem.IsChecked;
            }
        }

        private void btn_select_Click(object sender, RoutedEventArgs e)
        {
            SetIndex((int)txt_indx.Value);
        }

        private void SetIndex(int selectedidx)
        {
            foreach (ICameraDevice connectedDevice in ServiceProvider.DeviceManager.ConnectedDevices)
            {
                int index = 1;
                foreach (FileItem fileItem in Items)
                {
                    if (fileItem.Device == connectedDevice)
                    {
                        if (index == selectedidx)
                            fileItem.IsChecked = !fileItem.IsChecked;
                        index++;
                    }
                }
            }
        }

        private void button1_Click(object sender, RoutedEventArgs e)
        {
            dlg.Show();
            Thread thread = new Thread(PopulateImageList);
            thread.Start();
        }
    }

    public class DownloadableItems
    {
        public AsyncObservableCollection<FileItem> Items { get; set; }
        public ICameraDevice Device { get; set; }

        public DownloadableItems()
        {
            Items = new AsyncObservableCollection<FileItem>();
        }
    }
}
﻿using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading;

namespace ProcessedFileDeleteService
{
    public partial class Service1 : ServiceBase
    {
        private bool runnning;
        private Thread FolderDeleteServiceThread;
        private readonly string DeleteType = ConfigurationManager.AppSettings["DeleteType"].ToString();
        private readonly string appPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        private double days = Convert.ToDouble(ConfigurationManager.AppSettings["Days"].ToString()) * (-1);
        private int Minutes = Convert.ToInt32(ConfigurationManager.AppSettings["Minutes"].ToString()) * (-1);
        private readonly string AccessType = ConfigurationManager.AppSettings["AccessType"].ToString();
        private readonly string path = ConfigurationManager.AppSettings["RootFolderPath"].ToString();
        private readonly string target = "processed";
        private readonly string FTPHostIp = ConfigurationManager.AppSettings["FTPHostIp"].ToString();
        private readonly string FTPUserName = ConfigurationManager.AppSettings["FTPUserName"].ToString();
        private readonly string FTPPassword = ConfigurationManager.AppSettings["FTPPassword"].ToString();
        private readonly string targetExtension = "xml";
        int ServiceRunTime = Convert.ToInt32(ConfigurationManager.AppSettings["ServiceRunTime"].ToString());
        public Service1()
        {
            InitializeComponent();
        }

        protected override void OnStart(string[] args)
        {
            Thread.CurrentThread.Name = "MainThread";
            if (!Directory.Exists(appPath + "\\Logs\\"))
            {
                _ = Directory.CreateDirectory(appPath + "\\Logs\\");
            }
            runnning = true;
            FolderDeleteServiceThread = new Thread(new ThreadStart(CheckFTPLocal))
            {
                Name = "Processed file Delete Service Thread"
            };
            FolderDeleteServiceThread.Start();
            Logger.WriteDebugLog("Processed file Delete Service Thread");
        }


        public void CheckFTPLocal()
        {
            //read from config file
            while (runnning)
            {
                if (DeleteType.Equals("Minutes", StringComparison.OrdinalIgnoreCase))
                {
                    //Convert minutes to days
                    days = Minutes / 1440;
                }

                if (AccessType.Equals("FTP", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        //SSHClient(string hostIP, string userName, string password)
                        SSHClient sSHClient = new SSHClient(FTPHostIp, FTPUserName, FTPPassword);
                        _ = sSHClient.Connect();
                        if (sSHClient.sshClient.IsConnected)
                        {
                            DeleteFilesFTP(path, target, sSHClient);
                        }
                        else
                        {
                            Logger.WriteErrorLog("Unable to connect to FTP");
                        }

                    }
                    catch (Exception ex)
                    {
                        Logger.WriteErrorLog(ex.ToString());
                    }
                }
                else
                {
                    //check if at least one folder exists after root folder in path string 
                    bool folderexistis = path.Split('\\').Skip(1).Any();
                    if (!folderexistis)
                    {
                        Logger.WriteErrorLog("No folder exists after root folder in path string");
                        return;
                    }
                    else
                    {
                        DeleteFilesLocal(path, target);
                    }
                }

#if !DEBUG
                Thread.Sleep(1000 * 60 * ServiceRunTime);
#endif

            }
        }
        private void DeleteFilesFTP(string path, string target, SSHClient sSHClient)
        {
            System.Collections.Generic.IEnumerable<Renci.SshNet.Sftp.SftpFile> Directory = sSHClient.ListAllFiles(path);
            if (Directory == null)
            {
                return;
            }
            foreach (Renci.SshNet.Sftp.SftpFile subDirectory in Directory)
            {
                // if subdirectory if a file skip
                if (subDirectory.IsDirectory)
                {
                    //check if the directory is processed
                    string last = subDirectory.FullName.Split('/').Last();
                    if (last.Equals(target, StringComparison.OrdinalIgnoreCase))
                    {
                        //get all files in the directory
                        System.Collections.Generic.IEnumerable<Renci.SshNet.Sftp.SftpFile> files = sSHClient.ListAllFiles(subDirectory.FullName);
                        int count = 0;
                        foreach (Renci.SshNet.Sftp.SftpFile file in files)
                        {
                            if (!file.IsDirectory)
                            {
                                //check if the file is xml and older than 30 days
                                string fileextension = file.FullName.Split('.').Last();
                                if (fileextension.Equals(targetExtension, StringComparison.OrdinalIgnoreCase) && (sSHClient.GetFileCreationTime(file) < DateTime.Now.AddDays(days)))
                                {
                                    //delete the file
                                    sSHClient.deleteFile(file.FullName);
                                    count++;
                                }
                            }

                        }
                        if (count > 0)
                        {
                            //log total folders
                            Logger.WriteDebugLog("Number of files in the directory " + subDirectory.FullName + " is " + files.Count());
                            //log the number of files deleted
                            Logger.WriteDebugLog("Deleted " + count + " files from " + subDirectory.FullName);
                        }
                    }
                    else
                    {
                        // if subdirectory name is  . or .. skip 
                        if (!(subDirectory.Name.Equals(".") || subDirectory.Name.Equals("..")))
                        {
                            DeleteFilesFTP(subDirectory.FullName, target, sSHClient);
                        }

                    }
                }

            }

        }

        private void DeleteFilesLocal(string path, string target)
        {
            try
            {
                foreach (string subDirectory in Directory.GetDirectories(path, target, SearchOption.AllDirectories))
                {
                    string last = subDirectory.Split('\\').Last();
                    if (last.Equals(target, StringComparison.OrdinalIgnoreCase))
                    {
                        int count = 0;
                        int filecount = Directory.GetFiles(subDirectory).Count();
                        foreach (string file in Directory.GetFiles(subDirectory))
                        {
                            if (file.EndsWith(targetExtension, StringComparison.OrdinalIgnoreCase) && (File.GetLastWriteTime(file) < DateTime.Now.AddDays(days)))
                            {
                                File.Delete(file);
                                count++;
                            }
                        }
                        if (count > 0)
                        {
                            Logger.WriteDebugLog($"Number of files in the directory {subDirectory} is {filecount}");
                            Logger.WriteDebugLog($"Deleted {count} files from {subDirectory}");
                        }
                    }
                }
            }

            catch (UnauthorizedAccessException)
            {
                Logger.WriteErrorLog($"Access denied: {path}");
            }
            catch (Exception ex)
            {
                Logger.WriteErrorLog($"Error deleting {path}: {ex.Message}");
            }
        }




        protected override void OnStop()
        {
            runnning = false;
            FolderDeleteServiceThread.Abort();
            Thread.CurrentThread.Name = "MainThread";
            Logger.WriteDebugLog("Service Stopped");
        }

        public void OnDebug()
        {
            OnStart(null);
        }
    }
}

using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.ServiceProcess;
using System.Threading;

namespace ProcessedFileDeleteService
{
    public partial class Service1 : ServiceBase
    {
        private bool runnning;
        private Thread FolderDeleteServiceThread;
        private readonly string appPath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        private readonly int days = Convert.ToInt32(ConfigurationManager.AppSettings["Days"].ToString()) * (-1);
        private readonly string AccessType = ConfigurationManager.AppSettings["AccessType"].ToString();
        private readonly string path = ConfigurationManager.AppSettings["RootFolderPath"].ToString();
        private readonly string target = "processed";
        private readonly string FTPHostIp = ConfigurationManager.AppSettings["FTPHostIp"].ToString();
        private readonly string FTPUserName = ConfigurationManager.AppSettings["FTPUserName"].ToString();
        private readonly string FTPPassword = ConfigurationManager.AppSettings["FTPPassword"].ToString();
        private readonly int Port = Convert.ToInt32(ConfigurationManager.AppSettings["FTPPort"].ToString());
        private readonly string targetExtension = "xml";
        private readonly FtpWebResponse response;

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

                    DeleteFilesLocal(path, target);
                }

                #if !DEBUG
                Thread.Sleep(1000 * 60 * 60 * 24);
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
                        foreach (string file in Directory.GetFiles(subDirectory))
                        {
                            if (file.EndsWith(targetExtension, StringComparison.OrdinalIgnoreCase) && (System.IO.File.GetCreationTime(file) < DateTime.Now.AddDays(days)))
                            {
                                System.IO.File.Delete(file);
                                count++;
                            }
                        }
                        if (count > 0)
                        {
                            Logger.WriteDebugLog($"Number of files in the directory {subDirectory} is {Directory.GetFiles(subDirectory).Count()}");
                            Logger.WriteDebugLog($"Deleted {count} files from {subDirectory}" + DateTime.Now.ToString());
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
            FolderDeleteServiceThread.Join();
            Thread.CurrentThread.Name = "MainThread";
            Logger.WriteDebugLog("Service Stopped");
        }

        public void OnDebug()
        {
            OnStart(null);
        }
    }
}

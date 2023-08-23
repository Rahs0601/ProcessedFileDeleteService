using Renci.SshNet;
using Renci.SshNet.Sftp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ProcessedFileDeleteService
{
    public class SSHClient
    {
        private readonly string host = string.Empty;
        private readonly string user = string.Empty;
        private readonly string pass = string.Empty;
        private readonly int port = 22;
        public SftpClient sshClient;
        public SSHClient(string hostIP, string userName, string password)
        {
            host = hostIP; user = userName; pass = password;
        }

        #region  Download Files
        public void Download(SftpFile file, string destination, bool recursive = false)
        {
            try
            {
                if (!file.IsDirectory && !file.IsSymbolicLink)
                {
                    using (Stream fileStream = File.OpenWrite(Path.Combine(destination, file.Name)))
                    {
                        sshClient.DownloadFile(file.FullName, fileStream);
                    }
                }

                else if (file.IsSymbolicLink)
                {
                    Console.WriteLine("Symbolic link ignored: {0}", file.FullName);
                }

                //else if (file.Name != "." && file.Name != "..")
                //{
                //    var directory = Directory.CreateDirectory(Path.Combine(destination, file.Name));
                //    if (recursive)
                //    {
                //        IEnumerable<SftpFile> sftpFiles = ListAllFiles(file.FullName);
                //        foreach (var fls in sftpFiles)
                //        {
                //            Download(fls, directory.FullName);
                //        }
                //    }
                //}
            }
            catch (Exception ex)
            {
                Logger.WriteErrorLog(ex.ToString());
            }
        }

        public static void DownloadFile(SftpFile file, string directory)
        {
            using (Stream fileStream = File.OpenWrite(Path.Combine(directory, file.Name)))
            {
                //sshClient.DownloadFile(file.FullName, fileStream);
            }
        }
        #endregion

        #region GetFileCreationTime
        public DateTime GetFileCreationTime(SftpFile file)
        {
            return file.LastWriteTime;
        }
        #endregion

        #region MoveFile
        public void MoveFile(SftpFile file, string directory)
        {
            if (!sshClient.Exists(directory))
            {
                sshClient.CreateDirectory(directory);
            }

            if (file.IsRegularFile)
            {
                bool eachFileExistsInArchive = CheckIfRemoteFileExists(directory, file.Name);
                if (eachFileExistsInArchive)
                {
                    deleteFile(string.Format("{0}/{1}", directory, file.Name));
                    //eachFileNameInArchive = eachFileNameInArchive + "_" + DateTime.Now.ToString("MMddyyyy_HHmmss");//Change file name if the file already exists
                }
                file.MoveTo(string.Format("{0}/{1}", directory, file.Name));
            }
        }

        public bool CheckIfRemoteFileExists(string remoteFolderName, string remotefileName)
        {
            bool isFileExists = sshClient
                                .ListDirectory(remoteFolderName)
                                .Any(
                                        f => f.IsRegularFile &&
                                        f.Name.ToLower() == remotefileName.ToLower()
                                    );
            return isFileExists;
        }

        #endregion

        #region UploadFile
        public bool Upload(string destination, string source)
        {
            sshClient.ChangeDirectory(destination);
            using (FileStream fileStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                sshClient.BufferSize = 4 * 1024; // bypass Payload error large files
                try
                {
                    sshClient.UploadFile(fileStream, Path.GetFileName(source));
                    Logger.WriteDebugLog(string.Format("Uploaded file {0} to FTP", source));
                    return true;
                }
                catch (Exception exx)
                {
                    Logger.WriteErrorLog(exx.ToString());
                    return false;
                }
            }
        }
        #endregion

        #region Delete File
        public void deleteFile(string deletePath)
        {
            try
            {
                sshClient.DeleteFile(deletePath);
            }
            catch (Exception ex)
            {
                Logger.WriteErrorLog(ex.ToString());
            }
        }
        #endregion

        #region ListAllFiles
        public IEnumerable<SftpFile> ListAllFiles(string remoteDirectory)
        {
            IEnumerable<SftpFile> sftpFiles = null;
            try
            {
                sftpFiles = sshClient.ListDirectory(remoteDirectory);
            }
            catch (Exception e)
            {
                Logger.WriteErrorLog(e.ToString());
            }
            return sftpFiles;

        }
        #endregion

        #region Connect
        public bool Connect()
        {
            try
            {
                KeyboardInteractiveAuthenticationMethod kauth = new KeyboardInteractiveAuthenticationMethod(user);
                PasswordAuthenticationMethod pauth = new PasswordAuthenticationMethod(user, pass);
                kauth.AuthenticationPrompt += new EventHandler<Renci.SshNet.Common.AuthenticationPromptEventArgs>(HandleKeyEvent);

                ConnectionInfo connectionInfo = new ConnectionInfo(host, port, user, pauth, kauth);

                sshClient = new SftpClient(connectionInfo)
                {
                    KeepAliveInterval = TimeSpan.FromSeconds(60)
                };
                sshClient.Connect();
                return true;
            }
            catch (Exception ex)
            {
                Logger.WriteErrorLog(ex.ToString());
            }
            return false;
        }

        public void HandleKeyEvent(object sender, Renci.SshNet.Common.AuthenticationPromptEventArgs e)
        {
            foreach (Renci.SshNet.Common.AuthenticationPrompt prompt in e.Prompts)
            {
                if (prompt.Request.IndexOf("Password:", StringComparison.InvariantCultureIgnoreCase) != -1)
                {
                    prompt.Response = pass;
                }
            }
        }

        #endregion

        #region Disconnect
        public void DisConnect()
        {
            if (sshClient.IsConnected)
            {
                sshClient.Disconnect();
                sshClient.Dispose();
            }
        }
        #endregion
    }
}

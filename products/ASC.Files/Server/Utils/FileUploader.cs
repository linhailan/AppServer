/*
 *
 * (c) Copyright Ascensio System Limited 2010-2018
 *
 * This program is freeware. You can redistribute it and/or modify it under the terms of the GNU 
 * General Public License (GPL) version 3 as published by the Free Software Foundation (https://www.gnu.org/copyleft/gpl.html). 
 * In accordance with Section 7(a) of the GNU GPL its Section 15 shall be amended to the effect that 
 * Ascensio System SIA expressly excludes the warranty of non-infringement of any third-party rights.
 *
 * THIS PROGRAM IS DISTRIBUTED WITHOUT ANY WARRANTY; WITHOUT EVEN THE IMPLIED WARRANTY OF MERCHANTABILITY OR
 * FITNESS FOR A PARTICULAR PURPOSE. For more details, see GNU GPL at https://www.gnu.org/copyleft/gpl.html
 *
 * You can contact Ascensio System SIA by email at sales@onlyoffice.com
 *
 * The interactive user interfaces in modified source and object code versions of ONLYOFFICE must display 
 * Appropriate Legal Notices, as required under Section 5 of the GNU GPL version 3.
 *
 * Pursuant to Section 7 § 3(b) of the GNU GPL you must retain the original ONLYOFFICE logo which contains 
 * relevant author attributions when distributing the software. If the display of the logo in its graphic 
 * form is not reasonably feasible for technical reasons, you must include the words "Powered by ONLYOFFICE" 
 * in every copy of the program you distribute. 
 * Pursuant to Section 7 § 3(e) we decline to grant you any rights under trademark law for use of our trademarks.
 *
*/


using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading;

using ASC.Core;
using ASC.Core.Users;
using ASC.Files.Core;
using ASC.Files.Core.Security;
using ASC.MessagingSystem;
using ASC.Web.Core.Files;
using ASC.Web.Files.Classes;
using ASC.Web.Files.Helpers;
using ASC.Web.Files.Resources;
using ASC.Web.Studio.Core;
using ASC.Web.Studio.UserControls.Statistics;
using ASC.Web.Studio.Utility;

using Microsoft.Extensions.DependencyInjection;

using File = ASC.Files.Core.File;

namespace ASC.Web.Files.Utils
{
    public class FileUploader
    {
        public FilesSettingsHelper FilesSettingsHelper { get; }
        public FileUtility FileUtility { get; }
        public UserManager UserManager { get; }
        public TenantManager TenantManager { get; }
        public AuthContext AuthContext { get; }
        public SetupInfo SetupInfo { get; }
        public TenantExtra TenantExtra { get; }
        public TenantStatisticsProvider TenantStatisticsProvider { get; }
        public FileMarker FileMarker { get; }
        public FileConverter FileConverter { get; }
        public IDaoFactory DaoFactory { get; }
        public Global Global { get; }
        public FilesLinkUtility FilesLinkUtility { get; }
        public FilesMessageService FilesMessageService { get; }
        public FileSecurity FileSecurity { get; }
        public EntryManager EntryManager { get; }
        public IServiceProvider ServiceProvider { get; }
        public ChunkedUploadSessionHolder ChunkedUploadSessionHolder { get; }

        public FileUploader(
            FilesSettingsHelper filesSettingsHelper,
            FileUtility fileUtility,
            UserManager userManager,
            TenantManager tenantManager,
            AuthContext authContext,
            SetupInfo setupInfo,
            TenantExtra tenantExtra,
            TenantStatisticsProvider tenantStatisticsProvider,
            FileMarker fileMarker,
            FileConverter fileConverter,
            IDaoFactory daoFactory,
            Global global,
            FilesLinkUtility filesLinkUtility,
            FilesMessageService filesMessageService,
            FileSecurity fileSecurity,
            EntryManager entryManager,
            IServiceProvider serviceProvider,
            ChunkedUploadSessionHolder chunkedUploadSessionHolder)
        {
            FilesSettingsHelper = filesSettingsHelper;
            FileUtility = fileUtility;
            UserManager = userManager;
            TenantManager = tenantManager;
            AuthContext = authContext;
            SetupInfo = setupInfo;
            TenantExtra = tenantExtra;
            TenantStatisticsProvider = tenantStatisticsProvider;
            FileMarker = fileMarker;
            FileConverter = fileConverter;
            DaoFactory = daoFactory;
            Global = global;
            FilesLinkUtility = filesLinkUtility;
            FilesMessageService = filesMessageService;
            FileSecurity = fileSecurity;
            EntryManager = entryManager;
            ServiceProvider = serviceProvider;
            ChunkedUploadSessionHolder = chunkedUploadSessionHolder;
        }

        public File Exec(string folderId, string title, long contentLength, Stream data)
        {
            return Exec(folderId, title, contentLength, data, !FilesSettingsHelper.UpdateIfExist);
        }

        public File Exec(string folderId, string title, long contentLength, Stream data, bool createNewIfExist, bool deleteConvertStatus = true)
        {
            if (contentLength <= 0)
                throw new Exception(FilesCommonResource.ErrorMassage_EmptyFile);

            var file = VerifyFileUpload(folderId, title, contentLength, !createNewIfExist);

            var dao = DaoFactory.FileDao;
            file = dao.SaveFile(file, data);

            FileMarker.MarkAsNew(file);

            if (FileConverter.EnableAsUploaded && FileConverter.MustConvert(file))
                FileConverter.ExecAsync(file, deleteConvertStatus);

            return file;
        }

        public File VerifyFileUpload(string folderId, string fileName, bool updateIfExists, string relativePath = null)
        {
            fileName = Global.ReplaceInvalidCharsAndTruncate(fileName);

            if (Global.EnableUploadFilter && !FileUtility.ExtsUploadable.Contains(FileUtility.GetFileExtension(fileName)))
                throw new NotSupportedException(FilesCommonResource.ErrorMassage_NotSupportedFormat);

            folderId = GetFolderId(folderId, string.IsNullOrEmpty(relativePath) ? null : relativePath.Split('/').ToList());

            var fileDao = DaoFactory.FileDao;
            var file = fileDao.GetFile(folderId, fileName);

            if (updateIfExists && CanEdit(file))
            {
                file.Title = fileName;
                file.ConvertedType = null;
                file.Comment = FilesCommonResource.CommentUpload;
                file.Version++;
                file.VersionGroup++;
                file.Encrypted = false;

                return file;
            }

            var newFile = ServiceProvider.GetService<File>();
            newFile.FolderID = folderId;
            newFile.Title = fileName;
            return newFile;
        }

        public File VerifyFileUpload(string folderId, string fileName, long fileSize, bool updateIfExists)
        {
            if (fileSize <= 0)
                throw new Exception(FilesCommonResource.ErrorMassage_EmptyFile);

            var maxUploadSize = GetMaxFileSize(folderId);

            if (fileSize > maxUploadSize)
                throw FileSizeComment.GetFileSizeException(maxUploadSize);

            var file = VerifyFileUpload(folderId, fileName, updateIfExists);
            file.ContentLength = fileSize;
            return file;
        }

        private bool CanEdit(File file)
        {
            return file != null
                   && FileSecurity.CanEdit(file)
                   && !UserManager.GetUsers(AuthContext.CurrentAccount.ID).IsVisitor(UserManager)
                   && !EntryManager.FileLockedForMe(file.ID)
                   && !FileTracker.IsEditing(file.ID)
                   && file.RootFolderType != FolderType.TRASH
                   && !file.Encrypted;
        }

        private string GetFolderId(object folderId, IList<string> relativePath)
        {
            var folderDao = DaoFactory.FolderDao;
            var folder = folderDao.GetFolder(folderId);

            if (folder == null)
                throw new DirectoryNotFoundException(FilesCommonResource.ErrorMassage_FolderNotFound);

            if (!FileSecurity.CanCreate(folder))
                throw new SecurityException(FilesCommonResource.ErrorMassage_SecurityException_Create);

            if (relativePath != null && relativePath.Any())
            {
                var subFolderTitle = Global.ReplaceInvalidCharsAndTruncate(relativePath.FirstOrDefault());

                if (!string.IsNullOrEmpty(subFolderTitle))
                {
                    folder = folderDao.GetFolder(subFolderTitle, folder.ID);

                    if (folder == null)
                    {
                        var newFolder = ServiceProvider.GetService<Folder>();
                        newFolder.Title = subFolderTitle;
                        newFolder.ParentFolderID = folderId;

                        folderId = folderDao.SaveFolder(newFolder);

                        folder = folderDao.GetFolder(folderId);
                        FilesMessageService.Send(folder, MessageAction.FolderCreated, folder.Title);
                    }

                    folderId = folder.ID;

                    relativePath.RemoveAt(0);
                    folderId = GetFolderId(folderId, relativePath);
                }
            }

            return folderId.ToString();
        }

        #region chunked upload

        public File VerifyChunkedUpload(string folderId, string fileName, long fileSize, bool updateIfExists, string relativePath = null)
        {
            var maxUploadSize = GetMaxFileSize(folderId, true);

            if (fileSize > maxUploadSize)
                throw FileSizeComment.GetFileSizeException(maxUploadSize);

            var file = VerifyFileUpload(folderId, fileName, updateIfExists, relativePath);
            file.ContentLength = fileSize;

            return file;
        }

        public ChunkedUploadSession InitiateUpload(string folderId, string fileId, string fileName, long contentLength, bool encrypted)
        {
            if (string.IsNullOrEmpty(folderId))
                folderId = null;

            if (string.IsNullOrEmpty(fileId))
                fileId = null;

            var file = ServiceProvider.GetService<File>();
            file.ID = fileId;
            file.FolderID = folderId;
            file.Title = fileName;
            file.ContentLength = contentLength;

            var dao = DaoFactory.FileDao;
            var uploadSession = dao.CreateUploadSession(file, contentLength);

            uploadSession.Expired = uploadSession.Created + ChunkedUploadSessionHolder.SlidingExpiration;
            uploadSession.Location = FilesLinkUtility.GetUploadChunkLocationUrl(uploadSession.Id);
            uploadSession.TenantId = TenantManager.GetCurrentTenant().TenantId;
            uploadSession.UserId = AuthContext.CurrentAccount.ID;
            uploadSession.FolderId = folderId;
            uploadSession.CultureName = Thread.CurrentThread.CurrentUICulture.Name;
            uploadSession.Encrypted = encrypted;

            ChunkedUploadSessionHolder.StoreSession(uploadSession);

            return uploadSession;
        }

        public ChunkedUploadSession UploadChunk(string uploadId, Stream stream, long chunkLength)
        {
            var uploadSession = ChunkedUploadSessionHolder.GetSession(uploadId);
            uploadSession.Expired = DateTime.UtcNow + ChunkedUploadSessionHolder.SlidingExpiration;

            if (chunkLength <= 0)
            {
                throw new Exception(FilesCommonResource.ErrorMassage_EmptyFile);
            }

            if (chunkLength > SetupInfo.ChunkUploadSize)
            {
                throw FileSizeComment.GetFileSizeException(SetupInfo.MaxUploadSize(TenantExtra, TenantStatisticsProvider));
            }

            var maxUploadSize = GetMaxFileSize(uploadSession.FolderId, uploadSession.BytesTotal > 0);

            if (uploadSession.BytesUploaded + chunkLength > maxUploadSize)
            {
                AbortUpload(uploadSession);
                throw FileSizeComment.GetFileSizeException(maxUploadSize);
            }

            var dao = DaoFactory.FileDao;
            dao.UploadChunk(uploadSession, stream, chunkLength);

            if (uploadSession.BytesUploaded == uploadSession.BytesTotal)
            {
                FileMarker.MarkAsNew(uploadSession.File);
                ChunkedUploadSessionHolder.RemoveSession(uploadSession);
            }
            else
            {
                ChunkedUploadSessionHolder.StoreSession(uploadSession);
            }

            return uploadSession;
        }

        public void AbortUpload(string uploadId)
        {
            AbortUpload(ChunkedUploadSessionHolder.GetSession(uploadId));
        }

        private void AbortUpload(ChunkedUploadSession uploadSession)
        {
            DaoFactory.FileDao.AbortUploadSession(uploadSession);

            ChunkedUploadSessionHolder.RemoveSession(uploadSession);
        }

        private long GetMaxFileSize(object folderId, bool chunkedUpload = false)
        {
            var folderDao = DaoFactory.FolderDao;
            return folderDao.GetMaxUploadSize(folderId, chunkedUpload);
        }

        #endregion
    }
}
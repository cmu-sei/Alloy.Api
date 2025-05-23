// Copyright 2021 Carnegie Mellon University. All Rights Reserved.
// Released under a MIT (SEI)-style license. See LICENSE.md in the project root for license information.

using Microsoft.AspNetCore.Http;
using Caster.Api.Client;
using Alloy.Api.Infrastructure.Extensions;
using Alloy.Api.Infrastructure.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Alloy.Api.Services
{
    public interface ICasterService
    {
        // Task<IEnumerable<View>> GetViewsAsync(CancellationToken ct);
        Task<IEnumerable<Directory>> GetDirectoriesAsync(CancellationToken ct);
        Task<IEnumerable<Resource>> GetWorkspaceResourcesAsync(Guid workspaceId, CancellationToken ct);
        Task<object> GetWorkspaceOutputsAsync(Guid workspaceId, CancellationToken ct);
        Task<Resource> RefreshResourceAsync(Guid workspaceId, Resource resource, CancellationToken ct);
        // Task<IEnumerable<Workspace>> GetWorkspacesAsync(CancellationToken ct);
        // Task<Workspace> CreateWorkspaceInDirectoryAsync(Guid directoryId, string varsFileContent, CancellationToken ct);
    }

    public class CasterService : ICasterService
    {
        private readonly ICasterApiClient _casterApiClient;
        private readonly Guid _userId;
        private readonly string _userName;

        public CasterService(IHttpContextAccessor httpContextAccessor, ClientOptions clientSettings, ICasterApiClient casterApiClient)
        {
            _userId = httpContextAccessor.HttpContext.User.GetId();
            _userName = httpContextAccessor.HttpContext.User.Claims.First(c => c.Type.ToLower() == "name").Value;
            _casterApiClient = casterApiClient;
        }

        // public async Task<IEnumerable<View>> GetViewsAsync(CancellationToken ct)
        // {
        //     var views = await _casterApiClient.GetAllViewsAsync(ct);

        //     return views;
        // }

        public async Task<IEnumerable<Directory>> GetDirectoriesAsync(CancellationToken ct)
        {
            var directories = await _casterApiClient.GetAllDirectoriesAsync(false, false, ct);

            return directories;
        }

        public async Task<IEnumerable<Resource>> GetWorkspaceResourcesAsync(Guid workspaceId, CancellationToken ct)
        {
            return await _casterApiClient.GetResourcesByWorkspaceAsync(workspaceId, ct);
        }

        public async Task<object> GetWorkspaceOutputsAsync(Guid workspaceId, CancellationToken ct)
        {
            return (await _casterApiClient.GetWorkspaceOutputsAsync(workspaceId, ct)).Outputs;
        }

        public async Task<Resource> RefreshResourceAsync(Guid workspaceId, Resource resource, CancellationToken ct)
        {
            return await _casterApiClient.GetResourceAsync(workspaceId, resource.Id, resource.Type, ct);
        }

        // public async Task<IEnumerable<Workspace>> GetWorkspacesAsync(CancellationToken ct)
        // {
        //     var directories = await _casterApiClient.GetWorkspacesAsync(ct);

        //     return directories;
        // }

        // public async Task<Workspace> CreateWorkspaceInDirectoryAsync(Guid directoryId, string varsFileContent, CancellationToken ct)
        // {
        //     // remove special characters from the user name
        //     var userName = Regex.Replace(_userName, @"[^\w\.-]", "", RegexOptions.None);
        //     // create the new workspace
        //     var workspaceCommand = new CreateWorkspaceCommand()
        //     {
        //         Name = $"x-{userName}-{_userId.ToString()}",
        //         DirectoryId = directoryId
        //     };
        //     var newWorkspace = await _casterApiClient.CreateWorkspaceAsync(workspaceCommand, ct);
        //     // create the workspace variable file
        //     var createFileCommand = new CreateFileCommand()
        //     {
        //         Name = $"{workspaceCommand.Name}.auto.tfvars",
        //         DirectoryId = directoryId,
        //         WorkspaceId = newWorkspace.Id,
        //         Content = varsFileContent
        //     };
        //     await _casterApiClient.CreateFileAsync(createFileCommand, ct);

        //     return newWorkspace;
        // }

    }
}

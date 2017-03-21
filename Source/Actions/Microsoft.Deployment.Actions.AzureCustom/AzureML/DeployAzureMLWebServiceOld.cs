﻿
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AzureML;
using AzureML.Contract;
using Microsoft.Deployment.Common.ActionModel;
using Microsoft.Deployment.Common.Actions;
using Microsoft.Deployment.Common.Helpers;


namespace Microsoft.Deployment.Actions.AzureCustom.AzureML
{
    [Export(typeof(IAction))]
    public class DeployAzureMLWebServiceOld : BaseAction
    {
        public override async Task<ActionResponse> ExecuteActionAsync(ActionRequest request)
        {
            var azureToken = request.DataStore.GetJson("AzureToken", "access_token");
            var subscription = request.DataStore.GetJson("SelectedSubscription", "SubscriptionId");
            var workspaceName = request.DataStore.GetValue("WorkspaceName");
            var experimentName = request.DataStore.GetValue("ExperimentName");

            ManagementSDK azuremlClient = new ManagementSDK();
            var workspaces = azuremlClient.GetWorkspacesFromRdfe(azureToken, subscription);
            var workspace = workspaces.SingleOrDefault(p => p.Name.ToLowerInvariant() == workspaceName.ToLowerInvariant());

            if (workspace == null)
            {
                return new ActionResponse(ActionStatus.Failure, null, null, string.Empty, "Workspace not found");
            }

            var workspaceSettings = new WorkspaceSetting()
            {
                AuthorizationToken = workspace.AuthorizationToken.PrimaryToken,
                Location = workspace.Region,
                WorkspaceId = workspace.Id
            };

            var experiments = azuremlClient.GetExperiments(workspaceSettings);
            var experiment = experiments.LastOrDefault(p => p.Description.ToLowerInvariant() == experimentName.ToLowerInvariant());

            if (experiment == null)
            {
                return new ActionResponse(ActionStatus.Failure, null, null, string.Empty, "Experiment not found");
            }

            var webservice = azuremlClient.DeployWebServiceFromPredictiveExperiment(workspaceSettings, experiment.ExperimentId, true);
            request.DataStore.AddToDataStore("AzureMLWebService", JsonUtility.GetJObjectFromObject(webservice));
            return new ActionResponse(ActionStatus.Success);
        }
    }
}
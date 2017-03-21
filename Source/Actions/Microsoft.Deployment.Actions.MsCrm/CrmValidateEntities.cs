﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.ServiceModel;
using System.Text;
using System.Threading.Tasks;
using Hyak.Common.Internals;
using Microsoft.Deployment.Common.ActionModel;
using Microsoft.Deployment.Common.Helpers;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.WebServiceClient;
using Newtonsoft.Json.Linq;

namespace Microsoft.Deployment.Common.Actions.MsCrm
{
    [Export(typeof(IAction))]
    class CrmValidateEntities : BaseAction
    {
        public override async Task<ActionResponse> ExecuteActionAsync(ActionRequest request)
        {
            string refreshToken = request.DataStore.GetJson("MsCrmToken")["refresh_token"].ToString();
            string organizationUrl = request.DataStore.GetValue("OrganizationUrl");
            string[] entities = request.DataStore.GetValue("Entities").Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            var crmToken = RetrieveCrmOnlineToken(refreshToken, request.Info.WebsiteRootUrl, request.DataStore, organizationUrl);

            var proxy = new OrganizationWebProxyClient(new Uri($"{organizationUrl}XRMServices/2011/Organization.svc/web"), true)
            {
                HeaderToken = crmToken["access_token"].ToString()
            };

            List<string> retryEntities = entities.ToList();

            for (int i = 0; i < 3; i++)
            {
                try
                {
                    retryEntities = this.CheckAndUpdateEntities(retryEntities, proxy);
                }
                catch (AggregateException e)
                {
                    string output = string.Empty;
                    foreach (var ex in e.InnerExceptions)
                    {
                        output += ex.Message + " [" + ex.Data["entity"] + "]. ";
                    }
                    return new ActionResponse(ActionStatus.Failure, JsonUtility.GetJObjectFromObject(e), e, "DefaultErrorCode", output);
                }
                if (retryEntities.Count() == 0)
                {
                    break;
                }
            }

            return new ActionResponse(ActionStatus.Success);
        }
        
        public List<string> CheckAndUpdateEntities(List<string> entities, OrganizationWebProxyClient proxy)
        {
            List<string> entitiesToRetry = new List<string>();

            Parallel.ForEach(entities, (entity) =>
            {
                try
                {
                    var checkRequest = new RetrieveEntityRequest()
                    {
                        LogicalName = entity,
                        EntityFilters = EntityFilters.Entity
                    };

                    var checkResponse = new RetrieveEntityResponse();
                    checkResponse = (RetrieveEntityResponse)proxy.Execute(checkRequest);

                    if (checkResponse.EntityMetadata.ChangeTrackingEnabled != null &&
                        !(bool)checkResponse.EntityMetadata.ChangeTrackingEnabled &&
                        checkResponse.EntityMetadata.CanChangeTrackingBeEnabled.Value)
                    {
                        var updateRequest = new UpdateEntityRequest()
                        {
                            Entity = checkResponse.EntityMetadata
                        };

                        updateRequest.Entity.ChangeTrackingEnabled = true;

                        try
                        {
                            var updateResponse = new UpdateEntityResponse();
                            updateResponse = (UpdateEntityResponse)proxy.Execute(updateRequest);
                        }
                        catch
                        {
                            entitiesToRetry.Add(entity);
                        }
                    }
                }
                catch (FaultException e)
                {
                    e.Data.Add("entity", entity);
                    throw;
                }
            });

            return entitiesToRetry;
        }

        private JObject RetrieveCrmOnlineToken(string refreshToken, string websiteRootUrl, DataStore dataStore, string resourceUri)
        {
            string tokenUrl = string.Format(Constants.AzureTokenUri, dataStore.GetValue("AADTenant"));

            using (HttpClient httpClient = new HttpClient())
            {
                // CRM Online token
                string token = GetDynamicsResourceUri(refreshToken, resourceUri, websiteRootUrl, Constants.MsCrmClientId);
                StringContent content = new StringContent(token);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/x-www-form-urlencoded");
                string response = httpClient.PostAsync(new Uri(tokenUrl), content).Result.Content.AsString();

                return JsonUtility.GetJsonObjectFromJsonString(response);
            }
        }

        private string GetDynamicsResourceUri(string code, string uri, string rootUrl, string clientId)
        {
            return $"refresh_token={code}&" +
                   $"client_id={clientId}&" +
                   $"client_secret={Uri.EscapeDataString(Constants.MicrosoftClientSecret)}&" +
                   $"resource={Uri.EscapeDataString(uri)}&" +
                   $"redirect_uri={Uri.EscapeDataString(rootUrl + Constants.WebsiteRedirectPath)}&" +
                   "grant_type=refresh_token";
        }
    }
}

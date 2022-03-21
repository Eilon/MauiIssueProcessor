using GraphQL.Client.Http;
using GraphQL.Client.Serializer.Newtonsoft;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace CreateMikLabelModel.DL.Common
{
    public static class CommonHelper
    {
        public static string GetGitHubAuthToken()
        {
            const string UserSecretKey = "GitHubAccessToken";

            var config = new ConfigurationBuilder()
                .AddUserSecrets("MauiIssueProcessor.App")
                .Build();

            var gitHubAccessToken = config[UserSecretKey];
            if (string.IsNullOrEmpty(gitHubAccessToken))
            {
                throw new InvalidOperationException($"Couldn't find User Secret named '{UserSecretKey}' in configuration.");
            }
            return gitHubAccessToken;
        }

        public static GraphQLHttpClient CreateGraphQLClient()
        {
            var gitHubAccessToken = CommonHelper.GetGitHubAuthToken();

            var graphQLHttpClient = new GraphQLHttpClient("https://api.github.com/graphql", new NewtonsoftJsonSerializer());
            graphQLHttpClient.HttpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue(
                    scheme: "bearer",
                    parameter: gitHubAccessToken);
            return graphQLHttpClient;
        }
    }
}

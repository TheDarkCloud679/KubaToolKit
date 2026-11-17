using Amazon;
using Amazon.APIGateway;
using Amazon.APIGateway.Model;
using Amazon.Runtime.CredentialManagement;
using KubaToolKit.Modules.ApiClient.Models;

namespace KubaToolKit.Modules.ApiClient;

// Reconstructs the real, externally-used URL of every API Gateway REST
// API in the account -- not the raw {id}.execute-api... host, which is
// rarely what's actually called, but the custom domain + base path
// mapping on top of it (API Gateway: Custom domain names > API mappings).
public class AwsApiGatewayService
{
    public async Task<List<DiscoveredApiEndpoint>>
    DiscoverEndpoints(
        string profile,
        CancellationToken cancellationToken = default)
    {
        var chain = new CredentialProfileStoreChain();

        if (!chain.TryGetAWSCredentials(profile, out var credentials))
        {
            throw new Exception($"Unable to load AWS profile '{profile}'");
        }

        using var client =
            new AmazonAPIGatewayClient(credentials, RegionEndpoint.EUWest3);

        var apiNames = await LoadRestApiNames(client, cancellationToken);
        var domains = await LoadDomainNames(client, cancellationToken);

        var results = new List<DiscoveredApiEndpoint>();

        foreach (var domain in domains)
        {
            cancellationToken.ThrowIfCancellationRequested();

            results.AddRange(
                await LoadEndpointsForDomain(
                    client,
                    domain,
                    apiNames,
                    cancellationToken));
        }

        return results
            .OrderBy(r => r.ApiName)
            .ThenBy(r => r.Stage)
            .ToList();
    }

    private static async Task<Dictionary<string, string>>
    LoadRestApiNames(
        AmazonAPIGatewayClient client,
        CancellationToken cancellationToken)
    {
        var apiNames = new Dictionary<string, string>();
        string? position = null;

        do
        {
            var response =
                await client.GetRestApisAsync(
                    new GetRestApisRequest { Position = position, Limit = 500 },
                    cancellationToken);

            foreach (var api in response.Items)
            {
                apiNames[api.Id] = api.Name;
            }

            position = response.Position;
        }
        while (!string.IsNullOrEmpty(position));

        return apiNames;
    }

    private static async Task<List<string>>
    LoadDomainNames(
        AmazonAPIGatewayClient client,
        CancellationToken cancellationToken)
    {
        var domains = new List<string>();
        string? position = null;

        do
        {
            var response =
                await client.GetDomainNamesAsync(
                    new GetDomainNamesRequest { Position = position, Limit = 500 },
                    cancellationToken);

            domains.AddRange(response.Items.Select(d => d.Name));

            position = response.Position;
        }
        while (!string.IsNullOrEmpty(position));

        return domains;
    }

    private static async Task<List<DiscoveredApiEndpoint>>
    LoadEndpointsForDomain(
        AmazonAPIGatewayClient client,
        string domain,
        Dictionary<string, string> apiNames,
        CancellationToken cancellationToken)
    {
        var endpoints = new List<DiscoveredApiEndpoint>();
        string? position = null;

        do
        {
            var response =
                await client.GetBasePathMappingsAsync(
                    new GetBasePathMappingsRequest
                    {
                        DomainName = domain,
                        Position = position,
                        Limit = 500
                    },
                    cancellationToken);

            foreach (var mapping in response.Items)
            {
                var apiName =
                    apiNames.TryGetValue(mapping.RestApiId, out var name)
                        ? name
                        : mapping.RestApiId;

                // API Gateway represents "no base path" as the literal
                // string "(none)", not an empty one.
                var basePath =
                    string.IsNullOrEmpty(mapping.BasePath) || mapping.BasePath == "(none)"
                        ? ""
                        : mapping.BasePath;

                var url =
                    string.IsNullOrEmpty(basePath)
                        ? $"https://{domain}"
                        : $"https://{domain}/{basePath}";

                endpoints.Add(
                    new DiscoveredApiEndpoint
                    {
                        ApiName = apiName,
                        ApiId = mapping.RestApiId,
                        Stage = mapping.Stage,
                        Domain = domain,
                        BasePath = basePath,
                        Url = url
                    });
            }

            position = response.Position;
        }
        while (!string.IsNullOrEmpty(position));

        return endpoints;
    }
}

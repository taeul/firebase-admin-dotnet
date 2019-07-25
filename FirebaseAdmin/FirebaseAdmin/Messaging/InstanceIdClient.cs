﻿// Copyright 2019, Google Inc. All rights reserved.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Http;
using Google.Apis.Json;
using Google.Apis.Util;
using Newtonsoft.Json;

/// <summary>
/// A helper class for interacting with the Firebase Instance ID service.Implements the FCM
/// topic management functionality.
/// </summary>
public sealed class InstanceIdClient : IDisposable
{
    private const string IidHost = "https://iid.googleapis.com";

    private const string IidSubscriberPath = "iid/v1:batchAdd";

    private const string IidUnsubscribePath = "iid/v1:batchRemove";

    private readonly ConfigurableHttpClient httpClient;

    private readonly HttpErrorHandler errorHandler;

    /// <summary>
    /// Initializes a new instance of the <see cref="InstanceIdClient"/> class.
    /// </summary>
    /// <param name="clientFactory">A default implentation of the HTTP client factory.</param>
    /// <param name="credential">An instance of the <see cref="GoogleCredential"/> GoogleCredential class.</param>
    /// <param name="projectId">The Project Id for FCM Messaging.</param>
    public InstanceIdClient(HttpClientFactory clientFactory, GoogleCredential credential, string projectId)
    {
        if (string.IsNullOrEmpty(projectId))
        {
            throw new ArgumentException(
                "Project ID is required to access messaging service. Use a service account "
                + "credential or set the project ID explicitly via AppOptions. Alternatively "
                + "you can set the project ID via the GOOGLE_CLOUD_PROJECT environment "
                + "variable.");
        }

        this.httpClient = clientFactory.ThrowIfNull(nameof(clientFactory))
            .CreateAuthorizedHttpClient(credential);

        this.errorHandler = new MessagingErrorHandler();
    }

    /// <summary>
    /// Subscribes a list of registration tokens to a topic.
    /// </summary>
    /// <param name="topic">The topic name to subscribe to. /topics/ will be prepended to the topic name provided if absent.</param>
    /// <param name="registrationTokens">A list of registration tokens to subscribe.</param>
    /// <returns>A task that completes with a <see cref="TopicManagementResponse"/>, giving details about the topic subscription operations.</returns>
    public async Task<TopicManagementResponse> SubscribeToTopicAsync(string topic, List<string> registrationTokens)
    {
        try
        {
            return await this.SendInstanceIdRequest(topic, registrationTokens, IidSubscriberPath).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw this.CreateExceptionFromResponse(e);
        }
        catch (IOException)
        {
            throw new FirebaseMessagingException(ErrorCode.Internal, "Error while calling IID backend service");
        }
    }

    /// <summary>
    /// Unsubscribes a list of registration tokens from a topic.
    /// </summary>
    /// <param name="topic">The topic name to unsubscribe from. /topics/ will be prepended to the topic name provided if absent.</param>
    /// <param name="registrationTokens">A list of registration tokens to unsubscribe.</param>
    /// <returns>A task that completes with a <see cref="TopicManagementResponse"/>, giving details about the topic unsubscription operations.</returns>
    public async Task<TopicManagementResponse> UnsubscribeFromTopicAsync(string topic, List<string> registrationTokens)
    {
        try
        {
            return await this.SendInstanceIdRequest(topic, registrationTokens, IidUnsubscribePath).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw this.CreateExceptionFromResponse(e);
        }
        catch (IOException)
        {
            throw new FirebaseMessagingException(ErrorCode.Internal, "Error while calling IID backend service");
        }
    }

    /// <summary>
    /// Dispose the HttpClient.
    /// </summary>
    public void Dispose()
    {
        this.httpClient.Dispose();
    }

    private async Task<TopicManagementResponse> SendInstanceIdRequest(string topic, List<string> registrationTokens, string path)
    {
        string url = $"{IidHost}/{path}";
        var body = new InstanceIdServiceRequest
        {
            Topic = this.GetPrefixedTopic(topic),
            RegistrationTokens = registrationTokens,
        };

        var request = new HttpRequestMessage()
        {
            Method = HttpMethod.Post,
            RequestUri = new Uri(url),
            Content = NewtonsoftJsonSerializer.Instance.CreateJsonHttpContent(body),
        };

        request.Headers.Add("access_token_auth", "true");

        try
        {
            var response = await this.httpClient.SendAsync(request, default(CancellationToken)).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            this.errorHandler.ThrowIfError(response, json);
            return JsonConvert.DeserializeObject<TopicManagementResponse>(json);
        }
        catch (HttpRequestException e)
        {
            throw this.CreateExceptionFromResponse(e);
        }
    }

    private FirebaseMessagingException CreateExceptionFromResponse(HttpRequestException e)
    {
        var temp = e.ToFirebaseException();
        return new FirebaseMessagingException(
            temp.ErrorCode,
            temp.Message,
            inner: temp.InnerException,
            response: temp.HttpResponse);
    }

    private string GetPrefixedTopic(string topic)
    {
        if (topic.StartsWith("/topics/"))
        {
            return topic;
        }
        else
        {
            return "/topics/" + topic;
        }
    }

    private class InstanceIdServiceRequest
    {
        [JsonProperty("to")]
        public string Topic { get; set; }

        [JsonProperty("registration_tokens")]
        public List<string> RegistrationTokens { get; set; }
    }
}
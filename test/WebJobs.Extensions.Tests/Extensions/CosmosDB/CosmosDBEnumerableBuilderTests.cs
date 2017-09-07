﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Azure.WebJobs.Extensions.CosmosDB;
using Microsoft.Azure.WebJobs.Extensions.Tests.Extensions.CosmosDB.Models;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Extensions.Tests.Extensions.CosmosDB
{
    public class CosmosDBEnumerableBuilderTests
    {
        private const string DatabaseName = "ItemDb";
        private const string CollectionName = "ItemCollection";
        private readonly Uri _expectedUri = UriFactory.CreateDocumentCollectionUri(DatabaseName, CollectionName);

        [Fact]
        public async Task ConvertAsync_Succeeds_NoContinuation()
        {
            Mock<ICosmosDBService> mockService;
            var builder = CreateBuilder<Document>(out mockService);

            mockService
                .Setup(m => m.ExecuteNextAsync<Document>(_expectedUri, It.IsAny<SqlQuerySpec>(), It.IsAny<string>()))
                .ReturnsAsync(new DocumentQueryResponse<Document>
                {
                    Results = GetDocumentCollection(5),
                    ResponseContinuation = null
                });

            CosmosDBAttribute attribute = new CosmosDBAttribute(DatabaseName, CollectionName)
            {
                SqlQuery = string.Empty,
                SqlQueryParameters = new SqlParameterCollection()
            };

            var results = await builder.ConvertAsync(attribute, CancellationToken.None);
            Assert.Equal(5, results.Count());
        }

        [Fact]
        public async Task ConvertAsync_Succeeds_WithContinuation()
        {
            Mock<ICosmosDBService> mockService;
            var builder = CreateBuilder<Document>(out mockService);

            var docCollection = GetDocumentCollection(17);

            mockService
                .SetupSequence(m => m.ExecuteNextAsync<Document>(_expectedUri, It.IsAny<SqlQuerySpec>(), It.IsAny<string>()))
                .ReturnsAsync(new DocumentQueryResponse<Document>
                {
                    Results = docCollection.Take(5),
                    ResponseContinuation = "1"
                })
                .ReturnsAsync(new DocumentQueryResponse<Document>
                {
                    Results = docCollection.Skip(5).Take(5),
                    ResponseContinuation = "2"
                }).ReturnsAsync(new DocumentQueryResponse<Document>
                {
                    Results = docCollection.Skip(10).Take(5),
                    ResponseContinuation = "3"
                }).ReturnsAsync(new DocumentQueryResponse<Document>
                {
                    Results = docCollection.Skip(15).Take(2),
                    ResponseContinuation = null
                });


            CosmosDBAttribute attribute = new CosmosDBAttribute(DatabaseName, CollectionName)
            {
                SqlQuery = string.Empty,
                SqlQueryParameters = new SqlParameterCollection()
            };

            var results = await builder.ConvertAsync(attribute, CancellationToken.None);
            Assert.Equal(17, results.Count());

            mockService.Verify(m => m.ExecuteNextAsync<Document>(_expectedUri, It.IsAny<SqlQuerySpec>(), It.IsAny<string>()), Times.Exactly(4));
        }

        [Fact]
        public async Task ConvertAsync_RethrowsException_IfNotFound()
        {
            Mock<ICosmosDBService> mockService;
            var builder = CreateBuilder<Item>(out mockService);

            mockService
                .Setup(m => m.ExecuteNextAsync<Item>(_expectedUri, It.IsAny<SqlQuerySpec>(), It.IsAny<string>()))
                .ThrowsAsync(CosmosDBTestUtility.CreateDocumentClientException((HttpStatusCode)404));

            CosmosDBAttribute attribute = new CosmosDBAttribute(DatabaseName, CollectionName)
            {
                SqlQuery = string.Empty,
                SqlQueryParameters = new SqlParameterCollection()
            };

            var ex = await Assert.ThrowsAsync<DocumentClientException>(() => builder.ConvertAsync(attribute, CancellationToken.None));
            Assert.Equal("NotFound", ex.Error.Code);

            mockService.Verify(m => m.ExecuteNextAsync<Item>(_expectedUri, It.IsAny<SqlQuerySpec>(), It.IsAny<string>()), Times.Once());
        }

        private static IEnumerable<Document> GetDocumentCollection(int count)
        {
            List<Document> items = new List<Document>();
            for (int i = 0; i < count; i++)
            {
                var doc = new Document { Id = i.ToString() };
                doc.SetPropertyValue("Text", $"Item {i}");

                items.Add(doc);
            }
            return items;
        }

        private static CosmosDBEnumerableBuilder<T> CreateBuilder<T>(out Mock<ICosmosDBService> mockService) where T : class
        {
            CosmosDBConfiguration config = new CosmosDBConfiguration();
            config.ConnectionString = "AccountEndpoint=https://someuri;AccountKey=c29tZV9rZXk=;";

            mockService = new Mock<ICosmosDBService>(MockBehavior.Strict);
            Mock<ICosmosDBServiceFactory> mockServiceFactory = new Mock<ICosmosDBServiceFactory>(MockBehavior.Strict);

            mockServiceFactory
                .Setup(m => m.CreateService(It.IsAny<string>()))
                .Returns(mockService.Object);

            config.CosmosDBServiceFactory = mockServiceFactory.Object;

            return new CosmosDBEnumerableBuilder<T>(config);
        }
    }
}
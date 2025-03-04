﻿using Algolia.Search.Models.Search;

using Microsoft.AspNetCore.Mvc;

using System.Text.Json;
using Umbraco.Cms.Integrations.Search.Algolia.Builders;
using Umbraco.Cms.Integrations.Search.Algolia.Migrations;
using Umbraco.Cms.Integrations.Search.Algolia.Models;
using Umbraco.Cms.Integrations.Search.Algolia.Services;
using Umbraco.Cms.Web.BackOffice.Controllers;
using Umbraco.Cms.Web.Common;
using Umbraco.Cms.Web.Common.Attributes;
using Umbraco.Extensions;

namespace Umbraco.Cms.Integrations.Search.Algolia.Controllers
{
    [PluginController("UmbracoCmsIntegrationsSearchAlgolia")]
    public class SearchController : UmbracoAuthorizedApiController
    {
        private readonly IAlgoliaIndexService _indexService;

        private readonly IAlgoliaSearchService<SearchResponse<Record>> _searchService;

        private readonly IAlgoliaIndexDefinitionStorage<AlgoliaIndex> _indexStorage;

        private readonly UmbracoHelper _umbracoHelper;

        public SearchController(IAlgoliaIndexService indexService, IAlgoliaSearchService<SearchResponse<Record>> searchService, 
            IAlgoliaIndexDefinitionStorage<AlgoliaIndex> indexStorage,
            UmbracoHelper umbracoHelper)
        {
            _indexService = indexService;
            
            _searchService = searchService;

            _indexStorage = indexStorage;

            _umbracoHelper = umbracoHelper;
        }

        [HttpGet]
        public IActionResult GetIndices()
        {
            var results = _indexStorage.Get().Select(p => new IndexConfiguration
            {
                Id = p.Id,
                Name = p.Name,
                ContentData = JsonSerializer.Deserialize<List<ContentData>>(p.SerializedData)
            });

            return new JsonResult(results);
        } 

        [HttpPost]
        public async Task<IActionResult> SaveIndex([FromBody] IndexConfiguration index)
        {
            try
            {
                _indexStorage.AddOrUpdate(new AlgoliaIndex
                {
                    Id = index.Id,
                    Name = index.Name,
                    SerializedData = JsonSerializer.Serialize(index.ContentData),
                    Date = DateTime.Now
                });

                var result = await _indexService.PushData(index.Name);

                return new JsonResult(result);
            }
            catch(Exception ex)
            {
                return new JsonResult(Result.Fail(ex.Message));
            }
        }

        [HttpPost]
        public async Task<IActionResult> BuildIndex([FromBody] IndexConfiguration indexConfiguration)
        {
            try
            {
                var index = _indexStorage.GetById(indexConfiguration.Id);

                var payload = new List<Record>();

                var indexContentData = JsonSerializer.Deserialize<List<ContentData>>(index.SerializedData);

                foreach (var contentDataItem in indexContentData)
                {
                    var contentItems = _umbracoHelper.ContentAtXPath($"//{contentDataItem.ContentType.Alias}");

                    foreach (var contentItem in contentItems)
                    {
                        var record = new RecordBuilder()
                            .BuildFromContent(contentItem, (p) => contentDataItem.Properties.Any(q => q.Alias == p.Alias))
                            .Build();

                        payload.Add(record);
                    }
                }

                var result = await _indexService.PushData(index.Name, payload);

                return new JsonResult(result);
            }
            catch (Exception ex)
            {
                return new JsonResult(Result.Fail(ex.Message));
            }
        }

        [HttpDelete]
        public async Task<IActionResult> DeleteIndex(int id)
        {
            try
            {
                var indexName = _indexStorage.GetById(id).Name;

                _indexStorage.Delete(id);

                await _indexService.DeleteIndex(indexName);

                return new JsonResult(Result.Ok());
            }
            catch(Exception ex)
            {
                return new JsonResult(Result.Fail(ex.Message));
            }
        }

        [HttpGet]
        public IActionResult Search(int indexId, string query)
        {
            var index = _indexStorage.GetById(indexId);

            var searchResults = _searchService.Search(index.Name, query);

            var response = new Response
            {
                ItemsCount = searchResults.NbHits,
                PagesCount = searchResults.NbPages,
                ItemsPerPage = searchResults.HitsPerPage,
                Hits = searchResults.Hits.Select(p => p.Data.ToDictionary(x => x.Key, y => y.Value.ToString())).ToList()
            };

            return new JsonResult(response);
        }
    }
}

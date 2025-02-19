﻿// Copyright (c) Microsoft. All rights reserved.

namespace MinimalApi.Extensions;

internal static class SearchClientExtensions
{
    internal static async Task<SupportingContentRecord[]> QueryDocumentsAsync(
        this SearchClient searchClient,
        string? query = null,
        float[]? embedding = null,
        RequestOverrides? overrides = null,
        CancellationToken cancellationToken = default)
    {
        var documentContents = string.Empty;
        var top = overrides?.Top ?? 3;
        var exclude_category = overrides?.ExcludeCategory;
        var filter = exclude_category == null ? string.Empty : $"category ne '{exclude_category}'";
        var useSemanticRanker = overrides?.SemanticRanker ?? false;
        var useSemanticCaptions = overrides?.SemanticCaptions ?? false;

        SearchOptions searchOption = useSemanticRanker
            ? new SearchOptions
            {
                Filter = filter,
                QueryType = SearchQueryType.Semantic,
                QueryLanguage = "en-us",
                QuerySpeller = "lexicon",
                SemanticConfigurationName = "default",
                Size = top,
                QueryCaption = useSemanticCaptions ? QueryCaptionType.Extractive : QueryCaptionType.None,
            }
            : new SearchOptions
            {
                Filter = filter,
                Size = top,
            };

        if (embedding != null && overrides?.RetrievalMode != "Text")
        {
            var k = useSemanticRanker ? 50 : top;
            var vectorQuery = new RawVectorQuery
            {
                // if semantic ranker is enabled, we need to set the rank to a large number to get more
                // candidates for semantic reranking
                KNearestNeighborsCount = useSemanticRanker ? 50 : top,
                Vector = embedding,
            };
            vectorQuery.Fields.Add("embedding");
            searchOption.VectorQueries.Add(vectorQuery);
        }

        var searchResultResponse = await searchClient.SearchAsync<SearchDocument>(
            query, searchOption, cancellationToken);
        if (searchResultResponse.Value is null)
        {
            throw new InvalidOperationException("fail to get search result");
        }

        SearchResults<SearchDocument> searchResult = searchResultResponse.Value;

        // Assemble sources here.
        // Example output for each SearchDocument:
        // {
        //   "@search.score": 11.65396,
        //   "id": "Northwind_Standard_Benefits_Details_pdf-60",
        //   "content": "x-ray, lab, or imaging service, you will likely be responsible for paying a copayment or coinsurance. The exact amount you will be required to pay will depend on the type of service you receive. You can use the Northwind app or website to look up the cost of a particular service before you receive it.\nIn some cases, the Northwind Standard plan may exclude certain diagnostic x-ray, lab, and imaging services. For example, the plan does not cover any services related to cosmetic treatments or procedures. Additionally, the plan does not cover any services for which no diagnosis is provided.\nIt’s important to note that the Northwind Standard plan does not cover any services related to emergency care. This includes diagnostic x-ray, lab, and imaging services that are needed to diagnose an emergency condition. If you have an emergency condition, you will need to seek care at an emergency room or urgent care facility.\nFinally, if you receive diagnostic x-ray, lab, or imaging services from an out-of-network provider, you may be required to pay the full cost of the service. To ensure that you are receiving services from an in-network provider, you can use the Northwind provider search ",
        //   "category": null,
        //   "sourcepage": "Northwind_Standard_Benefits_Details-24.pdf",
        //   "sourcefile": "Northwind_Standard_Benefits_Details.pdf"
        // }
        var sb = new List<SupportingContentRecord>();
        foreach (var doc in searchResult.GetResults())
        {
            doc.Document.TryGetValue("sourcepage", out var sourcePageValue);
            string? contentValue;
            try
            {
                if (useSemanticCaptions)
                {
                    var docs = doc.Captions.Select(c => c.Text);
                    contentValue = string.Join(" . ", docs);
                }
                else
                {
                    doc.Document.TryGetValue("content", out var value);
                    contentValue = (string)value;
                }
            }
            catch (ArgumentNullException)
            {
                contentValue = null;
            }

            if (sourcePageValue is string sourcePage && contentValue is string content)
            {
                content = content.Replace('\r', ' ').Replace('\n', ' ');
                sb.Add(new SupportingContentRecord(sourcePage,content));
            }
        }

        return sb.ToArray();
    }
}

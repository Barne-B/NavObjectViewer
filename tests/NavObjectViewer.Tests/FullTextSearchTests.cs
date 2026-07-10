using NavObjectViewer.Core.Models;
using NavObjectViewer.Core.Services;
using Xunit;

namespace NavObjectViewer.Tests;

public class FullTextSearchTests
{
    [Fact]
    public void Finds_text_with_correct_line_number()
    {
        using var f = SyntheticExport.CreateDefault();
        List<NavObject> objs = new ObjectIndexer().IndexFile(f.Path);

        List<SearchMatch> matches = new FullTextSearch()
            .Search(objs, "PROCEDURE PostSomething", ignoreCase: true, useRegex: false);

        SearchMatch m = Assert.Single(matches);
        Assert.Equal(80040, m.Object.Id);
        Assert.Equal(15, m.Line); // PROCEDURE is on line 15 of that object
    }

    [Fact]
    public void Respects_case_sensitivity()
    {
        using var f = SyntheticExport.CreateDefault();
        List<NavObject> objs = new ObjectIndexer().IndexFile(f.Path);
        var search = new FullTextSearch();

        Assert.Empty(search.Search(objs, "procedure", ignoreCase: false, useRegex: false));
        Assert.NotEmpty(search.Search(objs, "procedure", ignoreCase: true, useRegex: false));
    }

    [Fact]
    public void Supports_regex()
    {
        using var f = SyntheticExport.CreateDefault();
        List<NavObject> objs = new ObjectIndexer().IndexFile(f.Path);

        List<SearchMatch> matches = new FullTextSearch()
            .Search(objs, @"Post\w+@\d+", ignoreCase: false, useRegex: true);

        Assert.Contains(matches, x => x.Object.Id == 80040);
    }

    [Fact]
    public void Scope_limits_which_objects_are_searched()
    {
        using var f = SyntheticExport.CreateDefault();
        List<NavObject> objs = new ObjectIndexer().IndexFile(f.Path);
        List<NavObject> onlyTables = objs.Where(o => o.Type == NavObjectType.Table).ToList();

        List<SearchMatch> matches = new FullTextSearch()
            .Search(onlyTables, "PROCEDURE", ignoreCase: true, useRegex: false);

        Assert.Empty(matches); // the PROCEDURE lives in the codeunit, which is out of scope
    }

    [Fact]
    public void Finds_cp850_umlaut_text()
    {
        using var f = SyntheticExport.CreateDefault();
        List<NavObject> objs = new ObjectIndexer().IndexFile(f.Path);

        List<SearchMatch> matches = new FullTextSearch()
            .Search(objs, "Fälligkeit", ignoreCase: false, useRegex: false);

        Assert.Contains(matches, x => x.Object.Id == 3);
    }

    [Fact]
    public void SearchObjects_summarizes_per_object_with_counts()
    {
        using var f = SyntheticExport.CreateDefault();
        List<NavObject> objs = new ObjectIndexer().IndexFile(f.Path);

        // "OBJECT-PROPERTIES" appears once in every object → one hit each.
        List<ObjectHit> hits = new FullTextSearch()
            .SearchObjects(objs, "OBJECT-PROPERTIES", ignoreCase: false, useRegex: false);

        Assert.Equal(objs.Count, hits.Count);
        Assert.All(hits, h => Assert.Equal(1, h.Count));
        Assert.All(hits, h => Assert.True(h.FirstLine > 0));
    }

    [Fact]
    public void SearchObjects_counts_multiple_matches_in_one_object()
    {
        using var f = SyntheticExport.CreateDefault();
        List<NavObject> objs = new ObjectIndexer().IndexFile(f.Path);

        // '=' appears on the four OBJECT-PROPERTIES metadata lines of the codeunit.
        ObjectHit hit = new FullTextSearch()
            .SearchObjects(objs, "=", ignoreCase: false, useRegex: false)
            .Single(h => h.Object.Id == 80040);

        Assert.True(hit.Count >= 4);
    }

    [Fact]
    public void MatchesInObject_returns_all_lines_for_drill_in()
    {
        using var f = SyntheticExport.CreateDefault();
        List<NavObject> objs = new ObjectIndexer().IndexFile(f.Path);
        NavObject cod = objs.Single(o => o.Id == 80040);

        List<SearchMatch> matches = new FullTextSearch()
            .MatchesInObject(cod, "PROCEDURE", ignoreCase: true, useRegex: false);

        SearchMatch m = Assert.Single(matches);
        Assert.Equal(15, m.Line);
        Assert.Contains("PROCEDURE", m.Text);
    }

    [Fact]
    public void Search_is_not_capped_by_default()
    {
        using var f = SyntheticExport.CreateDefault();
        List<NavObject> objs = new ObjectIndexer().IndexFile(f.Path);

        // Every object has an OBJECT-PROPERTIES line; default (uncapped) must return them all.
        List<SearchMatch> matches = new FullTextSearch()
            .Search(objs, "OBJECT-PROPERTIES", ignoreCase: false, useRegex: false);

        Assert.Equal(objs.Count, matches.Count);
    }
}

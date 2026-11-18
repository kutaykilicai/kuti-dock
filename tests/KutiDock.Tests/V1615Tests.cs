using System.Collections.Generic;
using KutiDock.Core;
using Xunit;

public class AiBalancesTests
{
    [Fact]
    public void Kimi_ReadsAvailableBalance()
    {
        // Moonshot /v1/users/me/balance yanıtının biçimi (Kasa hesabının 2026-11-18 değeri)
        Assert.Equal(4.94233, AiBalances.ParseKimi("{\"code\":0,\"data\":{\"available_balance\":4.94233,\"voucher_balance\":0,\"cash_balance\":4.94233},\"scode\":\"0x0\",\"status\":true}"));
        Assert.Null(AiBalances.ParseKimi("{\"error\":{\"message\":\"x\"}}"));
    }

    [Fact]
    public void AllModels_PrefersUsd_ThenCredits()
    {
        Assert.Equal(1.25, AiBalances.ParseAllModels("{\"state\":\"ok\",\"paid_credits\":1250000,\"spendable_paid_credits\":1250000,\"paid_balance_usd\":1.25}"));
        Assert.Equal(9.97, AiBalances.ParseAllModels("{\"spendable_paid_credits\":9970000}"));
        Assert.Null(AiBalances.ParseAllModels("{\"error\":\"invalid_request\"}"));
    }

    [Fact]
    public void OpenRouter_CreditsMinusUsage_AndKeyLimit()
    {
        Assert.Equal(9.06, AiBalances.ParseOpenRouterCredits("{\"data\":{\"total_credits\":20,\"total_usage\":10.94}}"));
        Assert.Null(AiBalances.ParseOpenRouterCredits("{\"error\":{\"code\":403}}"));
        Assert.Equal(3.5, AiBalances.ParseOpenRouterKey("{\"data\":{\"limit\":5,\"limit_remaining\":3.5}}"));
        Assert.Null(AiBalances.ParseOpenRouterKey("{\"data\":{\"limit\":null,\"limit_remaining\":null}}"));
    }

    [Fact]
    public void Order_Names_Links()
    {
        Assert.Equal(new[] { AiProvider.AllModels, AiProvider.OpenRouter, AiProvider.Kimi, AiProvider.Gemini }, AiBalances.Order);
        Assert.Equal("https://dash.allmodels.io/overview", AiBalances.PageUrl(AiProvider.AllModels));
        Assert.Equal("https://openrouter.ai/settings/credits", AiBalances.PageUrl(AiProvider.OpenRouter));
        Assert.Equal("https://platform.kimi.ai/console/account", AiBalances.PageUrl(AiProvider.Kimi));
        Assert.Equal("https://gemini.google.com/usage", AiBalances.PageUrl(AiProvider.Gemini));
        Assert.Null(AiBalances.KeyName(AiProvider.Gemini)); // resmi API yok: anahtar da yok
        Assert.StartsWith("https://api.moonshot.ai/", AiBalances.KimiBalanceUrl);
    }

    [Fact]
    public void Money_And_Status()
    {
        Assert.Equal("$4.94", AiBalances.Money(4.94233));
        Assert.Equal("—", AiBalances.Money(null));
        var at = new DateTimeOffset(2026, 11, 18, 13, 42, 0, TimeSpan.FromHours(3));
        var s = AiBalances.Status(new AiBalance(AiProvider.Kimi, 4.94, at, null), at);
        Assert.StartsWith("güncellendi ", s);
        Assert.Equal("API anahtarı yok", AiBalances.Status(new AiBalance(AiProvider.AllModels, null, null, "API anahtarı yok"), at));
        Assert.Equal("okunuyor…", AiBalances.Status(null, at));
    }

    [Fact]
    public void Keys_ParseFiltersUnknownAndEmpty_MergeOverwrites()
    {
        var k = AiBalances.ParseKeys("{\"moonshot\":\"a\",\"OpenRouter\":\" b \",\"allmodels\":\"\",\"x\":\"y\",\"gemini\":\"z\"}");
        Assert.Equal(2, k.Count);
        Assert.Equal("a", k["moonshot"]); Assert.Equal("b", k["openrouter"]);
        var m = AiBalances.Merge(new Dictionary<string, string> { ["moonshot"] = "old", ["allmodels"] = "c" }, k);
        Assert.Equal("a", m["moonshot"]); Assert.Equal("c", m["allmodels"]); Assert.Equal("b", m["openrouter"]);
        Assert.Equal(m, AiBalances.ParseKeys(AiBalances.SerializeKeys(m)));
    }

    [Fact]
    public void Refresh_Intervals()
    {
        Assert.Equal(TimeSpan.FromMinutes(10), AiBalances.RefreshEvery);
        Assert.True(AiBalances.PanelMinGap < AiBalances.RefreshEvery);
    }
}

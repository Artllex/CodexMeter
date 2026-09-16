// Copyright (C) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#include "pch.h"
#include "CountingWidgetImpl.h"

namespace
{
    std::wstring ShortTokenCount(unsigned long long value)
    {
        std::wostringstream text;
        text.imbue(std::locale(""));
        if (value >= 1000000)
        {
            text << std::fixed << std::setprecision(value >= 10000000 ? 0 : 1)
                 << static_cast<double>(value) / 1000000.0 << L" mln";
        }
        else if (value >= 1000)
        {
            text << std::fixed << std::setprecision(value >= 100000 ? 0 : 1)
                 << static_cast<double>(value) / 1000.0 << L" tys.";
        }
        else
        {
            text << value;
        }
        return text.str();
    }

    bool IsValidRange(std::wstring const& value)
    {
        return value == L"7d" || value == L"24h" || value == L"8h" || value == L"1h";
    }

    std::wstring TallChart(std::vector<unsigned long long> const& values)
    {
        constexpr size_t width = 24;
        constexpr size_t height = 6;
        std::vector<unsigned long long> columns(width, 0);
        unsigned long long maximum = 0;
        for (size_t column = 0; column < width && !values.empty(); ++column)
        {
            auto source = (std::min)(values.size() - 1, column * values.size() / width);
            columns[column] = values[source];
            maximum = (std::max)(maximum, columns[column]);
        }

        std::wstring chart;
        for (size_t row = height; row > 0; --row)
        {
            for (auto value : columns)
            {
                auto level = maximum == 0 ? 0 : static_cast<size_t>(std::ceil(height * static_cast<double>(value) / maximum));
                chart.push_back(level >= row ? L'█' : (row == 1 ? L'▁' : L'⠀'));
            }
            if (row > 1) chart.push_back(L'\n');
        }
        return chart;
    }
}

CountingWidget::CountingWidget(winrt::hstring const& id, winrt::hstring const& state) : WidgetImplBase(id, state)
{
    State(state);
    try
    {
        auto saved = winrt::Windows::Data::Json::JsonObject::Parse(state);
        auto range = std::wstring(saved.GetNamedString(L"range", L"24h"));
        if (IsValidRange(range)) m_range = range;
    }
    catch (...) {}
}

// This function wil be invoked when the Increment button was clicked by the user.
void CountingWidget::OnActionInvoked(winrt::WidgetActionInvokedArgs actionInvokedArgs)
{
    if (actionInvokedArgs.Verb() == L"refresh" || actionInvokedArgs.Verb() == L"setRange")
    {
        try
        {
            auto inputs = winrt::Windows::Data::Json::JsonObject::Parse(actionInvokedArgs.Data());
            auto range = std::wstring(inputs.GetNamedString(L"range", m_range));
            if (IsValidRange(range)) m_range = range;
        }
        catch (...) {}
        State(winrt::hstring(L"{\"range\":\"" + m_range + L"\"}"));
        Refresh();
    }
}

// This function will be invoked when WidgetContext has changed.
void CountingWidget::OnWidgetContextChanged(winrt::WidgetContextChangedArgs /*contextChangedArgs*/)
{
    // (Optional) There a several things that can be done here:
    // 1. If you need to adjust template/data for the new context (i.e. widget size has chaned) - you can do it here.
    // 2. Log this call for telemetry to monitor what size users choose the most.
}

// This function will be invoked when widget is Activated.
void CountingWidget::Activate(winrt::WidgetContext /*widgetContext*/)
{
    m_isActivated = true;
    Refresh();
}

// This function will be invoked when widget is Deactivated.
void CountingWidget::Deactivate(winrt::hstring /*widgetId*/)
{
    // This is the moment to stop sending all further updates until
    // Activate() was called again.
    m_isActivated = false;
}

winrt::hstring CountingWidget::GetDefaultTemplate()
{
    return LR"({"type":"AdaptiveCard","$schema":"http://adaptivecards.io/schemas/adaptive-card.json","version":"1.6","body":[],"metadata":{"webUrl":"https://codexmeter.local/widget.html"}})";
}

winrt::hstring CountingWidget::GetTemplateForWidget()
{
    // This widget has the same template for all the sizes/themes so we load it only once.
    static winrt::hstring widgetTemplate = GetDefaultTemplate();
    return widgetTemplate;
}

winrt::hstring CountingWidget::GetDataForWidget()
{
    try
    {
        wchar_t localAppData[MAX_PATH]{};
        GetEnvironmentVariableW(L"LOCALAPPDATA", localAppData, MAX_PATH);
        std::filesystem::path sourcePath = std::filesystem::path(localAppData) / L"CodexMeter" / L"data" / L"latest.json";
        std::ifstream input(std::filesystem::path(sourcePath), std::ios::binary);
        std::string json((std::istreambuf_iterator<char>(input)), std::istreambuf_iterator<char>());
        auto root = winrt::Windows::Data::Json::JsonObject::Parse(winrt::to_hstring(json));
        auto limits = root.GetNamedObject(L"limits");
        auto buckets = limits.GetNamedObject(L"rateLimitsByLimitId", nullptr);
        auto bucket = buckets && buckets.HasKey(L"codex") ? buckets.GetNamedObject(L"codex") : limits.GetNamedObject(L"rateLimits");
        auto primary = bucket.GetNamedObject(L"primary");
        int used = static_cast<int>(std::round(primary.GetNamedNumber(L"usedPercent")));
        int remaining = std::clamp(100 - used, 0, 100);
        auto resetEpoch = static_cast<time_t>(primary.GetNamedNumber(L"resetsAt", 0));
        tm resetTime{};
        localtime_s(&resetTime, &resetEpoch);
        std::wstringstream reset;
        reset << std::put_time(&resetTime, L"%d.%m · %H:%M");

        std::wstring todayTokens = L"brak danych";
        auto usage = root.GetNamedObject(L"usage", nullptr);
        if (usage)
        {
            auto daily = usage.GetNamedArray(L"dailyUsageBuckets", nullptr);
            if (daily)
            {
                time_t now = time(nullptr); tm localNow{}; localtime_s(&localNow, &now);
                std::wstringstream today; today << std::put_time(&localNow, L"%Y-%m-%d");
                for (auto const& item : daily)
                {
                    auto row = item.GetObject();
                    if (row.GetNamedString(L"startDate", L"") == today.str())
                        todayTokens = std::to_wstring(static_cast<unsigned long long>(row.GetNamedNumber(L"tokens", 0)));
                }
            }
        }
        std::wstring updated = L"—";
        auto stamp = root.GetNamedString(L"fetchedAt", L"");
        if (stamp.size() >= 16) updated = std::wstring(stamp.c_str() + 11, 5);

        std::wstring chart = TallChart(std::vector<unsigned long long>(24, 0));
        std::wstring chartTotal = L"brak danych";
        std::wstring chartFrom = L"−24 h";
        std::wstring chartTo = L"teraz";
        auto ranges = root.GetNamedObject(L"widgetUsageRanges", nullptr);
        auto hourlyUsage = ranges ? ranges.GetNamedObject(m_range, nullptr) : nullptr;
        if (!hourlyUsage) hourlyUsage = root.GetNamedObject(L"widgetHourlyUsage", nullptr);
        if (hourlyUsage)
        {
            auto hourlyBuckets = hourlyUsage.GetNamedArray(L"buckets", nullptr);
            if (hourlyBuckets && hourlyBuckets.Size() > 0)
            {
                std::vector<unsigned long long> values;
                for (auto const& item : hourlyBuckets)
                {
                    auto value = static_cast<unsigned long long>(item.GetObject().GetNamedNumber(L"tokens", 0));
                    values.push_back(value);
                }
                chart = TallChart(values);
                chartTotal = ShortTokenCount(static_cast<unsigned long long>(hourlyUsage.GetNamedNumber(L"totalTokens", 0))) + L" tokenów";
                chartFrom = hourlyUsage.GetNamedString(L"from", L"−24 h");
                chartTo = hourlyUsage.GetNamedString(L"to", L"teraz");
            }
        }

        winrt::Windows::Data::Json::JsonObject data;
        data.Insert(L"remaining", winrt::Windows::Data::Json::JsonValue::CreateStringValue(std::to_wstring(remaining) + L"%"));
        data.Insert(L"used", winrt::Windows::Data::Json::JsonValue::CreateStringValue(std::to_wstring(used) + L"%"));
        data.Insert(L"reset", winrt::Windows::Data::Json::JsonValue::CreateStringValue(reset.str()));
        data.Insert(L"today", winrt::Windows::Data::Json::JsonValue::CreateStringValue(todayTokens));
        data.Insert(L"updated", winrt::Windows::Data::Json::JsonValue::CreateStringValue(updated));
        data.Insert(L"chart", winrt::Windows::Data::Json::JsonValue::CreateStringValue(chart));
        data.Insert(L"chartTotal", winrt::Windows::Data::Json::JsonValue::CreateStringValue(chartTotal));
        data.Insert(L"chartFrom", winrt::Windows::Data::Json::JsonValue::CreateStringValue(chartFrom));
        data.Insert(L"chartTo", winrt::Windows::Data::Json::JsonValue::CreateStringValue(chartTo));
        data.Insert(L"range", winrt::Windows::Data::Json::JsonValue::CreateStringValue(m_range));
        return data.Stringify();
    }
    catch (...)
    {
        return LR"({"remaining":"—","used":"—","reset":"brak danych","today":"brak danych","updated":"—","chart":"▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁","chartTotal":"brak danych","chartFrom":"−24 h","chartTo":"teraz","range":"24h"})";
    }
}

void CountingWidget::Refresh()
{
    winrt::WidgetUpdateRequestOptions updateOptions{ Id() };
    updateOptions.Template(GetTemplateForWidget());
    updateOptions.Data(GetDataForWidget());
    updateOptions.CustomState(State());
    winrt::WidgetManager::GetDefault().UpdateWidget(updateOptions);
}

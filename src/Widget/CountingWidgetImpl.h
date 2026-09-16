// Copyright (C) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#pragma once
#include "WidgetImplBase.h"

class CountingWidget : public WidgetImplBase
{
public:
    // Initalize a widget with saved state
    CountingWidget(winrt::hstring const& id, winrt::hstring const& state);

    void OnActionInvoked(winrt::WidgetActionInvokedArgs actionInvokedArgs) override;
    void OnWidgetContextChanged(winrt::WidgetContextChangedArgs contextChangedArgs) override;
    void Activate(winrt::WidgetContext widgetContext) override;
    void Deactivate(winrt::hstring widgetId) override;
    winrt::hstring GetTemplateForWidget() override;
    winrt::hstring GetDataForWidget() override;
private:
    winrt::hstring GetDefaultTemplate();
    void Refresh();
    std::wstring m_range{ L"24h" };
};

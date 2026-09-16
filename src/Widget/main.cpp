// Copyright (C) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

#include "pch.h"
#include "WidgetProvider.h"
#include "CountingWidgetImpl.h"
#include <mutex>

namespace
{
    void WriteActivationLog(std::string const& message)
    {
        wchar_t modulePath[MAX_PATH]{};
        if (!GetModuleFileNameW(nullptr, modulePath, MAX_PATH)) return;
        auto logPath = std::filesystem::path(modulePath).parent_path() / L"provider.log";
        std::ofstream output(logPath, std::ios::app | std::ios::binary);
        output << message << "\r\n";
    }
}

// This GUID is the same GUID that was provided in the 
// registration of the COM Server and Class Id in the .appxmanifest.
static constexpr GUID widget_provider_clsid
{ /* 101D03A3-6FC8-4887-9B64-310A7B164319 */
    0x101d03a3, 0x6fc8, 0x4887, {0x9b, 0x64, 0x31, 0x0a, 0x7b, 0x16, 0x43, 0x19}
};

wil::unique_event g_shudownEvent(wil::EventOptions::None);

void SignalLocalServerShutdown()
{
    g_shudownEvent.SetEvent();
}

// This is implementation of a ClassFactory that will instantiate WidgetProvider
// that will interact with the Widget Service.
template <typename T>
struct SingletonClassFactory : winrt::implements<SingletonClassFactory<T>, IClassFactory, winrt::no_module_lock>
{
    STDMETHODIMP CreateInstance(
        ::IUnknown* outer,
        GUID const& iid,
        void** result) noexcept final
    {
        WriteActivationLog("ClassFactory: CreateInstance begin");
        *result = nullptr;

        std::unique_lock lock(mutex);

        if (outer)
        {
            WriteActivationLog("ClassFactory: aggregation rejected");
            return CLASS_E_NOAGGREGATION;
        }

        try
        {
            auto value = winrt::make<T>();
            auto hr = value.as(iid, result);
            std::ostringstream details;
            details << "ClassFactory: QueryInterface result 0x" << std::hex << static_cast<uint32_t>(hr);
            WriteActivationLog(details.str());
            return hr;
        }
        catch (winrt::hresult_error const& error)
        {
            std::ostringstream details;
            details << "ClassFactory: HRESULT 0x" << std::hex << static_cast<uint32_t>(error.code().value)
                    << " " << winrt::to_string(error.message());
            WriteActivationLog(details.str());
            return error.code();
        }
        catch (...)
        {
            WriteActivationLog("ClassFactory: unknown exception");
            return E_FAIL;
        }
    }

    STDMETHODIMP LockServer(BOOL) noexcept final
    {
        return S_OK;
    }

private:
    std::mutex mutex;
};

int WINAPI wWinMain(_In_ HINSTANCE, _In_opt_ HINSTANCE, _In_ PWSTR commandLine, _In_ int)
{
    WriteActivationLog("Process: starting");
    winrt::init_apartment();

    if (commandLine && wcsstr(commandLine, L"--comprobe"))
    {
        static constexpr GUID widgetProviderInterface
        { /* 5C5774CC-72A0-452D-B9ED-075C0DD25EED */
            0x5c5774cc, 0x72a0, 0x452d, {0xb9, 0xed, 0x07, 0x5c, 0x0d, 0xd2, 0x5e, 0xed}
        };
        static constexpr GUID widgetResourceProviderInterface
        { /* DCF328C0-012C-40F5-BB28-3A1C714D027D */
            0xdcf328c0, 0x012c, 0x40f5, {0xbb, 0x28, 0x3a, 0x1c, 0x71, 0x4d, 0x02, 0x7d}
        };
        void* provider{};
        void* resourceProvider{};
        auto providerResult = CoCreateInstance(
            widget_provider_clsid,
            nullptr,
            CLSCTX_LOCAL_SERVER,
            widgetProviderInterface,
            &provider);
        auto resourceResult = CoCreateInstance(
            widget_provider_clsid,
            nullptr,
            CLSCTX_LOCAL_SERVER,
            widgetResourceProviderInterface,
            &resourceProvider);
        std::ostringstream details;
        details << "Packaged COM probe: provider=0x" << std::hex << static_cast<uint32_t>(providerResult)
                << " resources=0x" << static_cast<uint32_t>(resourceResult);
        WriteActivationLog(details.str());
        if (resourceProvider) static_cast<IUnknown*>(resourceProvider)->Release();
        if (provider) static_cast<IUnknown*>(provider)->Release();
        return SUCCEEDED(providerResult) && SUCCEEDED(resourceResult) ? 0 : 1;
    }

    if (commandLine && wcsstr(commandLine, L"--selftest"))
    {
        CountingWidget widget(L"selftest", L"");
        auto data = winrt::to_string(widget.GetDataForWidget());
        auto widgetTemplate = winrt::to_string(widget.GetTemplateForWidget());
        WriteActivationLog(std::string("Selftest data: ") + data);
        std::ofstream output("widget-selftest.json", std::ios::binary);
        output << data;
        return data.find("\"remaining\":\"—\"") == std::string::npos
            && data.find("\"used\":\"—\"") == std::string::npos
            && data.find("\"chartTotal\":\"brak danych\"") == std::string::npos
            && widgetTemplate.find("https://codexmeter.local/widget.html") != std::string::npos ? 0 : 1;
    }

    wil::unique_com_class_object_cookie widgetProviderFactory;
    // Create WidgetProvider factory
    auto factory = winrt::make<SingletonClassFactory<WidgetProvider>>();

    // CoRegister the WidgetProvider Factory with the GUID that was indicated in COM Server registration.
    winrt::check_hresult(CoRegisterClassObject(
        widget_provider_clsid,
        factory.get(),
        CLSCTX_LOCAL_SERVER,
        REGCLS_MULTIPLEUSE,
        widgetProviderFactory.put()));
    WriteActivationLog("Process: class object registered");

    DWORD index{};
    HANDLE events[] = { g_shudownEvent.get() };
    winrt::check_hresult(CoWaitForMultipleObjects(CWMO_DISPATCH_CALLS | CWMO_DISPATCH_WINDOW_MESSAGES, INFINITE,
        static_cast<ULONG>(std::size(events)), events, &index));

    return 0;
}

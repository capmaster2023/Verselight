#include <streams.h>
#include <iostream>
#include <cstring>
const CLSID CLSID_MyVirtualCam = {0xa9d47a18,0xc751,0x4bde,{0xab,0x6b,0xa3,0xd1,0x3f,0x09,0x05,0x01}};
CFactoryTemplate g_Templates[] = {{}};
int g_cTemplates = 0;
constexpr int Bytes = 1280 * 720 * 3;
class Sink final : public CBaseRenderer {
public:
    volatile LONG count = 0, good = 0;
    bool expected;
    Sink(HRESULT* hr, bool frame) : CBaseRenderer(CLSID_NULL, NAME("MyVirtualCam test sink"), nullptr, hr), expected(frame) { }
    HRESULT CheckMediaType(const CMediaType* t) override { return t->subtype == MEDIASUBTYPE_RGB24 ? S_OK : E_FAIL; }
    HRESULT DoRenderSample(IMediaSample* s) override {
        BYTE* b = nullptr; if (FAILED(s->GetPointer(&b)) || s->GetActualDataLength() != Bytes) return E_FAIL;
        if ((expected && b[0] == 33 && b[1] == 66 && b[2] == 99) || (!expected && b[0] == 0 && b[1] == 0 && b[2] == 0)) InterlockedIncrement(&good);
        InterlockedIncrement(&count); return S_OK;
    }
};
int wmain(int argc, wchar_t** argv) {
    bool expected = argc > 1 && wcscmp(argv[1], L"frame") == 0;
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    HANDLE mapping = nullptr, mutex = nullptr; BYTE* memory = nullptr;
    if (expected) {
        mapping = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, 64 + Bytes, L"Local\\MyChurch.MyVirtualCam.frames.v1");
        memory = static_cast<BYTE*>(MapViewOfFile(mapping, FILE_MAP_WRITE, 0, 0, 64 + Bytes));
        if (!memory) return 10;
        ZeroMemory(memory, 64); reinterpret_cast<DWORD*>(memory)[0] = 0x4D43414D; reinterpret_cast<DWORD*>(memory)[1] = 1;
        *reinterpret_cast<ULONGLONG*>(memory + 8) = GetTickCount64();
        reinterpret_cast<DWORD*>(memory)[4] = 1280; reinterpret_cast<DWORD*>(memory)[5] = 720; reinterpret_cast<DWORD*>(memory)[6] = 3840;
        for (int i = 0; i < Bytes; i += 3) { memory[64+i] = 33; memory[65+i] = 66; memory[66+i] = 99; }
        mutex = CreateMutexW(nullptr, FALSE, L"Local\\MyChurch.MyVirtualCam.lock.v1");
    }
    IGraphBuilder* graph = nullptr; IBaseFilter* camera = nullptr; IMediaControl* control = nullptr; IEnumPins* pins = nullptr; IPin* pin = nullptr;
    HRESULT hr = CoCreateInstance(CLSID_FilterGraph, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&graph));
    if (SUCCEEDED(hr)) hr = CoCreateInstance(CLSID_MyVirtualCam, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&camera));
    HRESULT sinkResult = S_OK; auto* sink = new Sink(&sinkResult, expected); sink->AddRef();
    if (SUCCEEDED(hr)) hr = graph->AddFilter(camera, L"MyVirtualCam");
    if (SUCCEEDED(hr)) hr = graph->AddFilter(sink, L"Sink");
    if (SUCCEEDED(hr)) hr = camera->EnumPins(&pins);
    if (SUCCEEDED(hr) && pins->Next(1, &pin, nullptr) != S_OK) hr = E_FAIL;
    if (SUCCEEDED(hr)) hr = graph->ConnectDirect(pin, sink->GetPin(0), nullptr);
    if (SUCCEEDED(hr)) hr = graph->QueryInterface(IID_PPV_ARGS(&control));
    if (SUCCEEDED(hr)) hr = control->Run();
    if (SUCCEEDED(hr)) Sleep(750);
    if (control) { control->Stop(); control->Release(); }
    LONG count = sink->count, good = sink->good;
    std::cout << "MyVirtualCam " << (expected ? "shared frame" : "app closed/black") << ": hr=" << std::hex << hr << std::dec << " samples=" << count << " good=" << good << std::endl;
    if (pin) pin->Release(); if (pins) pins->Release(); if (camera) camera->Release(); if (graph) graph->Release(); sink->Release();
    if (memory) UnmapViewOfFile(memory); if (mapping) CloseHandle(mapping); if (mutex) CloseHandle(mutex);
    CoUninitialize(); return SUCCEEDED(hr) && count >= 5 && good == count ? 0 : 1;
}

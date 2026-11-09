// MyChurch virtual video source. Uses Microsoft's MIT-licensed DirectShow base classes.
#include <streams.h>
#include <ks.h>
#include <ksmedia.h>
#include <algorithm>
#include <cstring>

// {A9D47A18-C751-4BDE-AB6B-A3D13F090501} -- permanent identity, never the OBS camera CLSID.
const CLSID CLSID_MyVirtualCam = {0xa9d47a18,0xc751,0x4bde,{0xab,0x6b,0xa3,0xd1,0x3f,0x09,0x05,0x01}};
constexpr int Width = 1280, Height = 720, Stride = Width * 3, Bytes = Stride * Height;
constexpr REFERENCE_TIME FrameTime = 10000000 / 30;
constexpr wchar_t Mapping[] = L"Local\\MyChurch.MyVirtualCam.frames.v1";
constexpr wchar_t Lock[] = L"Local\\MyChurch.MyVirtualCam.lock.v1";
struct FrameHeader { DWORD magic, version; ULONGLONG tick; DWORD width, height, stride, sequence; BYTE reserved[32]; };
static_assert(sizeof(FrameHeader) == 64);

class CaptureStream final : public CSourceStream, public IAMStreamConfig, public IKsPropertySet {
    HANDLE mapping_ = nullptr, mutex_ = nullptr;
    const BYTE* memory_ = nullptr;
    REFERENCE_TIME time_ = 0;
    ULONGLONG start_ = 0;
    void Open() {
        if (!mutex_) mutex_ = CreateMutexW(nullptr, FALSE, Lock);
        if (!mapping_) {
            mapping_ = OpenFileMappingW(FILE_MAP_READ, FALSE, Mapping);
            if (mapping_) {
                memory_ = static_cast<const BYTE*>(MapViewOfFile(mapping_, FILE_MAP_READ, 0, 0, 64 + Bytes));
                if (!memory_) { CloseHandle(mapping_); mapping_ = nullptr; }
            }
        }
    }
public:
    CaptureStream(HRESULT* result, CSource* filter) : CSourceStream(NAME("MyVirtualCam Capture"), result, filter, L"Capture") { }
    ~CaptureStream() override {
        if (memory_) UnmapViewOfFile(memory_);
        if (mapping_) CloseHandle(mapping_);
        if (mutex_) CloseHandle(mutex_);
    }
    DECLARE_IUNKNOWN;
    STDMETHODIMP NonDelegatingQueryInterface(REFIID iid, void** out) override {
        if (iid == IID_IAMStreamConfig) return GetInterface(static_cast<IAMStreamConfig*>(this), out);
        if (iid == IID_IKsPropertySet) return GetInterface(static_cast<IKsPropertySet*>(this), out);
        return CSourceStream::NonDelegatingQueryInterface(iid, out);
    }
    HRESULT GetMediaType(CMediaType* type) override {
        if (!type) return E_POINTER;
        auto* info = reinterpret_cast<VIDEOINFOHEADER*>(type->AllocFormatBuffer(sizeof(VIDEOINFOHEADER)));
        if (!info) return E_OUTOFMEMORY;
        ZeroMemory(info, sizeof(*info));
        info->bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
        info->bmiHeader.biWidth = Width; info->bmiHeader.biHeight = Height;
        info->bmiHeader.biPlanes = 1; info->bmiHeader.biBitCount = 24;
        info->bmiHeader.biCompression = BI_RGB; info->bmiHeader.biSizeImage = Bytes;
        info->AvgTimePerFrame = FrameTime; info->dwBitRate = Bytes * 8 * 30;
        type->SetType(&MEDIATYPE_Video); type->SetSubtype(&MEDIASUBTYPE_RGB24);
        type->SetFormatType(&FORMAT_VideoInfo); type->SetTemporalCompression(FALSE);
        type->SetSampleSize(Bytes); return S_OK;
    }
    HRESULT GetMediaType(int position, CMediaType* type) override {
        if (position < 0) return E_INVALIDARG;
        return position == 0 ? GetMediaType(type) : VFW_S_NO_MORE_ITEMS;
    }
    HRESULT CheckMediaType(const CMediaType* type) override {
        if (!type || type->majortype != MEDIATYPE_Video || type->subtype != MEDIASUBTYPE_RGB24 || type->formattype != FORMAT_VideoInfo || type->cbFormat < sizeof(VIDEOINFOHEADER)) return VFW_E_TYPE_NOT_ACCEPTED;
        auto* b = reinterpret_cast<const VIDEOINFOHEADER*>(type->pbFormat);
        return b->bmiHeader.biWidth == Width && b->bmiHeader.biHeight == Height && b->bmiHeader.biBitCount == 24 && b->bmiHeader.biCompression == BI_RGB ? S_OK : VFW_E_TYPE_NOT_ACCEPTED;
    }
    HRESULT DecideBufferSize(IMemAllocator* alloc, ALLOCATOR_PROPERTIES* properties) override {
        if (!alloc || !properties) return E_POINTER;
        properties->cBuffers = 2; properties->cbBuffer = Bytes;
        ALLOCATOR_PROPERTIES actual{};
        HRESULT hr = alloc->SetProperties(properties, &actual);
        if (FAILED(hr)) return hr;
        return actual.cbBuffer >= Bytes ? S_OK : E_FAIL;
    }
    HRESULT OnThreadCreate() override { time_ = 0; start_ = GetTickCount64(); return S_OK; }
    HRESULT FillBuffer(IMediaSample* sample) override {
        if (!sample) return E_POINTER;
        BYTE* data = nullptr; HRESULT hr = sample->GetPointer(&data);
        if (FAILED(hr)) return hr;
        if (sample->GetSize() < Bytes) return E_FAIL;
        // Limit the live source to 30 fps even in graphs with no reference clock.
        ULONGLONG due = start_ + static_cast<ULONGLONG>(time_ / 10000), now = GetTickCount64();
        if (due > now) Sleep(static_cast<DWORD>(std::min<ULONGLONG>(due - now, 34)));
        ZeroMemory(data, Bytes); // available forever, black when MyChurch is absent/blank/stale
        Open();
        if (memory_ && mutex_) {
            DWORD wait = WaitForSingleObject(mutex_, 5);
            if (wait == WAIT_OBJECT_0 || wait == WAIT_ABANDONED) {
                auto* h = reinterpret_cast<const FrameHeader*>(memory_);
                ULONGLONG tick = GetTickCount64();
                if (h->magic == 0x4D43414D && h->version == 1 && h->width == Width && h->height == Height && h->stride == Stride && h->tick > 0 && tick >= h->tick && tick - h->tick < 2000)
                    std::memcpy(data, memory_ + 64, Bytes);
                ReleaseMutex(mutex_);
            }
        }
        REFERENCE_TIME end = time_ + FrameTime;
        sample->SetTime(&time_, &end); time_ = end;
        sample->SetActualDataLength(Bytes); sample->SetSyncPoint(TRUE); return S_OK;
    }
    STDMETHODIMP SetFormat(AM_MEDIA_TYPE* format) override {
        if (!format) return E_POINTER;
        CMediaType t(*format); return CheckMediaType(&t); // fixed 720p30, no misleading accepted formats
    }
    STDMETHODIMP GetFormat(AM_MEDIA_TYPE** out) override {
        if (!out) return E_POINTER; CMediaType t; HRESULT hr = GetMediaType(&t);
        if (FAILED(hr)) return hr; *out = CreateMediaType(&t); return *out ? S_OK : E_OUTOFMEMORY;
    }
    STDMETHODIMP GetNumberOfCapabilities(int* count, int* size) override {
        if (!count || !size) return E_POINTER; *count = 1; *size = sizeof(VIDEO_STREAM_CONFIG_CAPS); return S_OK;
    }
    STDMETHODIMP GetStreamCaps(int index, AM_MEDIA_TYPE** out, BYTE* caps) override {
        if (!out || !caps) return E_POINTER; if (index != 0) return S_FALSE;
        auto* c = reinterpret_cast<VIDEO_STREAM_CONFIG_CAPS*>(caps); ZeroMemory(c, sizeof(*c));
        c->guid = FORMAT_VideoInfo; c->InputSize = {Width, Height}; c->MinCroppingSize = c->MaxCroppingSize = c->MinOutputSize = c->MaxOutputSize = {Width, Height};
        c->CropGranularityX = c->CropGranularityY = c->OutputGranularityX = c->OutputGranularityY = 1;
        c->MinFrameInterval = c->MaxFrameInterval = FrameTime; c->MinBitsPerSecond = c->MaxBitsPerSecond = Bytes * 8 * 30;
        return GetFormat(out);
    }
    STDMETHODIMP Set(REFGUID, DWORD, LPVOID, DWORD, LPVOID, DWORD) override { return E_NOTIMPL; }
    STDMETHODIMP Get(REFGUID set, DWORD id, LPVOID, DWORD, LPVOID data, DWORD size, DWORD* returned) override {
        if (set != AMPROPSETID_Pin) return E_PROP_SET_UNSUPPORTED;
        if (id != AMPROPERTY_PIN_CATEGORY) return E_PROP_ID_UNSUPPORTED;
        if (returned) *returned = sizeof(GUID);
        if (!data) return returned ? S_OK : E_POINTER;
        if (size < sizeof(GUID)) return E_UNEXPECTED;
        *static_cast<GUID*>(data) = PIN_CATEGORY_CAPTURE; return S_OK;
    }
    STDMETHODIMP QuerySupported(REFGUID set, DWORD id, DWORD* supported) override {
        if (set != AMPROPSETID_Pin) return E_PROP_SET_UNSUPPORTED;
        if (id != AMPROPERTY_PIN_CATEGORY) return E_PROP_ID_UNSUPPORTED;
        if (!supported) return E_POINTER; *supported = KSPROPERTY_SUPPORT_GET; return S_OK;
    }
};
class CaptureFilter final : public CSource, public IAMFilterMiscFlags {
public:
    CaptureFilter(LPUNKNOWN outer, HRESULT* hr) : CSource(NAME("MyVirtualCam"), outer, CLSID_MyVirtualCam) {
        auto* stream = new CaptureStream(hr, this); if (!stream) *hr = E_OUTOFMEMORY;
    }
    DECLARE_IUNKNOWN;
    static CUnknown* WINAPI Create(LPUNKNOWN outer, HRESULT* hr) { return new CaptureFilter(outer, hr); }
    STDMETHODIMP NonDelegatingQueryInterface(REFIID iid, void** out) override {
        if (iid == IID_IAMFilterMiscFlags) return GetInterface(static_cast<IAMFilterMiscFlags*>(this), out);
        return CSource::NonDelegatingQueryInterface(iid, out);
    }
    STDMETHODIMP_(ULONG) GetMiscFlags() override { return AM_FILTER_MISC_FLAGS_IS_SOURCE; }
};
CFactoryTemplate g_Templates[] = { { L"MyVirtualCam", &CLSID_MyVirtualCam, CaptureFilter::Create, nullptr, nullptr } };
int g_cTemplates = sizeof(g_Templates) / sizeof(g_Templates[0]);

static HRESULT RegisterCamera(bool install) {
    HRESULT init = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (FAILED(init) && init != RPC_E_CHANGED_MODE) return init;
    HRESULT hr = AMovieDllRegisterServer2(install ? TRUE : FALSE);
    if (SUCCEEDED(hr)) {
        IFilterMapper2* mapper = nullptr;
        hr = CoCreateInstance(CLSID_FilterMapper2, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&mapper));
        if (SUCCEEDED(hr)) {
            if (install) {
                REGPINTYPES types[] = { { &MEDIATYPE_Video, &MEDIASUBTYPE_RGB24 } };
                REGFILTERPINS2 pin{}; pin.dwFlags = REG_PINFLAG_B_OUTPUT; pin.cInstances = 1; pin.nMediaTypes = 1; pin.lpMediaType = types;
                pin.clsPinCategory = &PIN_CATEGORY_CAPTURE;
                REGFILTER2 filter{}; filter.dwVersion = 2; filter.dwMerit = MERIT_DO_NOT_USE; filter.cPins2 = 1; filter.rgPins2 = &pin;
                hr = mapper->RegisterFilter(CLSID_MyVirtualCam, L"MyVirtualCam", nullptr, &CLSID_VideoInputDeviceCategory, nullptr, &filter);
            } else hr = mapper->UnregisterFilter(&CLSID_VideoInputDeviceCategory, nullptr, CLSID_MyVirtualCam);
            mapper->Release();
        }
    }
    if (SUCCEEDED(init)) CoUninitialize();
    return hr;
}
STDAPI DllRegisterServer() { return RegisterCamera(true); }
STDAPI DllUnregisterServer() { return RegisterCamera(false); }
extern "C" BOOL WINAPI DllEntryPoint(HINSTANCE, ULONG, LPVOID);
BOOL APIENTRY DllMain(HINSTANCE module, DWORD reason, LPVOID reserved) { return DllEntryPoint(module, reason, reserved); }

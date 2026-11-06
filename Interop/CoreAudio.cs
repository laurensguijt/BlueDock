using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BlueDock.Interop
{
    /// <summary>
    /// Core Audio (MMDevice) interop, used to reach the Kernel Streaming filter behind a
    /// Bluetooth audio endpoint. Windows exposes two "one shot" KS properties on that filter
    /// that ask the Bluetooth stack to connect or drop the audio profiles of a device; this is
    /// the same mechanism the Windows volume flyout uses.
    ///
    /// IKsControl cannot be activated on the endpoint itself (that returns E_NOINTERFACE);
    /// the endpoint's topology has to be walked to the filter it is connected to first.
    /// </summary>
    internal static class CoreAudio
    {
        // ---------- COM declarations ----------

        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject { }

        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
            [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
            [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr client);
            [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr client);
        }

        [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int Item(int index, out IMMDevice device);
        }

        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams,
                                       [MarshalAs(UnmanagedType.IUnknown)] out object? iface);
            [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetState(out int state);
        }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetAt(int index, out PropertyKey key);
            [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
            [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
            [PreserveSig] int Commit();
        }

        [ComImport, Guid("2A07407E-6497-4A18-9787-32F79BD0D98F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDeviceTopology
        {
            [PreserveSig] int GetConnectorCount(out int count);
            [PreserveSig] int GetConnector(int index, out IConnector connector);
            [PreserveSig] int GetSubunitCount(out int count);
            [PreserveSig] int GetSubunit(int index, out IntPtr subunit);
            [PreserveSig] int GetPartById(int id, out IntPtr part);
            [PreserveSig] int GetDeviceId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetSignalPath(IntPtr from, IntPtr to, bool rejectMixedPaths, out IntPtr parts);
        }

        [ComImport, Guid("9C2C4058-23F5-41DE-877A-DF3AF236A09E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IConnector
        {
            [PreserveSig] int GetType(out int type);
            [PreserveSig] int GetDataFlow(out int flow);
            [PreserveSig] int ConnectTo(IConnector other);
            [PreserveSig] int Disconnect();
            [PreserveSig] int IsConnected(out bool connected);
            [PreserveSig] int GetConnectedTo(out IConnector other);
            [PreserveSig] int GetConnectorIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetDeviceIdConnectedTo([MarshalAs(UnmanagedType.LPWStr)] out string id);
        }

        /// <summary>
        /// Undocumented, but the only way to change the default playback device: Windows
        /// exposes no supported API for it, and every audio switcher on Windows uses this.
        /// </summary>
        [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
        private class PolicyConfigClient { }

        [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPolicyConfig
        {
            // Only SetDefaultEndpoint is called, but every slot before it has to be declared
            // so that the vtable offset lines up.
            [PreserveSig] int GetMixFormat(IntPtr a, IntPtr b);
            [PreserveSig] int GetDeviceFormat(IntPtr a, int b, IntPtr c);
            [PreserveSig] int ResetDeviceFormat(IntPtr a);
            [PreserveSig] int SetDeviceFormat(IntPtr a, IntPtr b, IntPtr c);
            [PreserveSig] int GetProcessingPeriod(IntPtr a, int b, IntPtr c, IntPtr d);
            [PreserveSig] int SetProcessingPeriod(IntPtr a, IntPtr b);
            [PreserveSig] int GetShareMode(IntPtr a, IntPtr b);
            [PreserveSig] int SetShareMode(IntPtr a, IntPtr b);
            [PreserveSig] int GetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
            [PreserveSig] int SetPropertyValue(IntPtr a, IntPtr b, IntPtr c);
            [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
            [PreserveSig] int SetEndpointVisibility(IntPtr a, int b);
        }

        [ComImport, Guid("28F54685-06FD-11D2-B27A-00A0C9223196"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IKsControl
        {
            [PreserveSig] int KsProperty(ref KsProperty property, int propertyLength,
                                         IntPtr propertyData, int dataLength, out int bytesReturned);
            [PreserveSig] int KsMethod(IntPtr method, int methodLength, IntPtr data, int dataLength, out int bytesReturned);
            [PreserveSig] int KsEvent(IntPtr evt, int eventLength, IntPtr data, int dataLength, out int bytesReturned);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey
        {
            public Guid FormatId;
            public int PropertyId;
            public PropertyKey(string formatId, int propertyId) { FormatId = new Guid(formatId); PropertyId = propertyId; }
        }

        /// <summary>Only the fields needed here; the union is two pointer sized slots (24 bytes total on x64).</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct PropVariant
        {
            public ushort Vt;
            public ushort Reserved1, Reserved2, Reserved3;
            public IntPtr Slot1;
            public IntPtr Slot2;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KsProperty
        {
            public Guid Set;
            public int Id;
            public int Flags;
        }

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant pvar);

        // ---------- Constants ----------

        private const int DataFlowRender = 0;
        private const int DataFlowCapture = 1;
        private const int DeviceStateAll = 0x0F;
        private const int DeviceStateActive = 0x01;
        /// <summary>eConsole, eMultimedia, eCommunications: all three, or the switch is half done.</summary>
        private static readonly int[] AllRoles = { 0, 1, 2 };
        private const int StgmRead = 0;
        private const int ClsCtxAll = 0x17;
        private const ushort VtClsid = 72;

        /// <summary>KSPROPSETID_BtAudio.</summary>
        private static readonly Guid KsPropSetIdBtAudio = new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");
        private const int KsPropertyOneShotReconnect = 0;
        private const int KsPropertyOneShotDisconnect = 1;
        private const int KsPropertyTypeGet = 0x00000001;

        private static readonly Guid IidKsControl = new("28F54685-06FD-11D2-B27A-00A0C9223196");
        private static readonly Guid IidDeviceTopology = new("2A07407E-6497-4A18-9787-32F79BD0D98F");

        /// <summary>DEVPKEY_Device_ContainerId: ties an audio endpoint to its Bluetooth device.</summary>
        private static readonly PropertyKey PkeyContainerId = new("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C", 2);

        // ---------- Default playback device ----------

        /// <summary>True when this device currently has a usable playback endpoint.</summary>
        public static bool HasActiveRenderEndpoint(Guid container)
        {
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                try
                {
                    return FindActiveRender(enumerator, container) != null;
                }
                finally { Release(enumerator); }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>True when Windows is currently playing sound through this device.</summary>
        public static bool IsDefaultRenderEndpoint(Guid container)
        {
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                try
                {
                    if (enumerator.GetDefaultAudioEndpoint(DataFlowRender, 0, out var current) != 0) return false;
                    try { return BelongsTo(current, container); }
                    finally { Release(current); }
                }
                finally { Release(enumerator); }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Routes Windows' sound to this device, for all three roles. Verified by reading the
        /// default back, because the call reports success either way.
        /// </summary>
        public static bool SetDefaultRenderEndpoint(Guid container)
        {
            try
            {
                var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
                try
                {
                    string? id = FindActiveRender(enumerator, container);
                    if (id == null) return false;

                    var policy = (IPolicyConfig)new PolicyConfigClient();
                    try
                    {
                        foreach (int role in AllRoles)
                            if (policy.SetDefaultEndpoint(id, role) < 0) return false;
                    }
                    finally { Release(policy); }
                }
                finally { Release(enumerator); }
            }
            catch
            {
                return false;
            }

            return IsDefaultRenderEndpoint(container);
        }

        /// <summary>Id of this device's active playback endpoint, or null when it has none.</summary>
        private static string? FindActiveRender(IMMDeviceEnumerator enumerator, Guid container)
        {
            if (enumerator.EnumAudioEndpoints(DataFlowRender, DeviceStateActive, out var collection) != 0) return null;
            if (collection.GetCount(out int count) != 0) return null;

            for (int i = 0; i < count; i++)
            {
                if (collection.Item(i, out var endpoint) != 0) continue;
                if (!BelongsTo(endpoint, container)) continue;
                if (endpoint.GetId(out string id) == 0) return id;
            }
            return null;
        }

        // ---------- Public API ----------

        /// <summary>True when the device container owns at least one audio endpoint we can control.</summary>
        public static bool HasAudioEndpoint(Guid container)
        {
            try
            {
                foreach (var _ in FindKsFilters(container, stopAtFirst: true))
                    return true;
            }
            catch
            {
                // Core Audio unavailable; treat as "no endpoint".
            }
            return false;
        }

        /// <summary>
        /// Asks the Bluetooth stack to connect or drop the audio profiles of this device container.
        /// A true result only means the request was accepted: the caller has to watch the real
        /// connection state to know whether the device actually came up.
        /// </summary>
        public static bool SendOneShot(Guid container, bool connect)
        {
            bool accepted = false;
            try
            {
                foreach (var ks in FindKsFilters(container, stopAtFirst: false))
                {
                    try
                    {
                        var prop = new KsProperty
                        {
                            Set = KsPropSetIdBtAudio,
                            Id = connect ? KsPropertyOneShotReconnect : KsPropertyOneShotDisconnect,
                            Flags = KsPropertyTypeGet,
                        };
                        int hr = ks.KsProperty(ref prop, Marshal.SizeOf<KsProperty>(), IntPtr.Zero, 0, out _);
                        if (hr >= 0) accepted = true;
                    }
                    catch
                    {
                        // Try the next filter.
                    }
                    finally
                    {
                        Release(ks);
                    }
                }
            }
            catch
            {
                return false;
            }
            return accepted;
        }

        // ---------- Internals ----------

        /// <summary>
        /// Every KS filter reachable from the audio endpoints of this device container.
        /// Caller owns the returned COM objects.
        /// </summary>
        private static IEnumerable<IKsControl> FindKsFilters(Guid container, bool stopAtFirst)
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            try
            {
                foreach (int flow in new[] { DataFlowRender, DataFlowCapture })
                {
                    if (enumerator.EnumAudioEndpoints(flow, DeviceStateAll, out var collection) != 0) continue;
                    if (collection.GetCount(out int count) != 0) continue;

                    for (int i = 0; i < count; i++)
                    {
                        if (collection.Item(i, out var endpoint) != 0) continue;
                        if (!BelongsTo(endpoint, container)) continue;

                        foreach (var ks in FiltersOf(enumerator, endpoint))
                        {
                            yield return ks;
                            if (stopAtFirst) yield break;
                        }
                    }
                }
            }
            finally
            {
                Release(enumerator);
            }
        }

        private static IEnumerable<IKsControl> FiltersOf(IMMDeviceEnumerator enumerator, IMMDevice endpoint)
        {
            var topologyIid = IidDeviceTopology;
            if (endpoint.Activate(ref topologyIid, ClsCtxAll, IntPtr.Zero, out object? topologyObj) != 0
                || topologyObj is not IDeviceTopology topology)
                yield break;

            try
            {
                if (topology.GetConnectorCount(out int connectors) != 0) yield break;

                for (int c = 0; c < connectors; c++)
                {
                    if (topology.GetConnector(c, out var connector) != 0) continue;
                    if (connector.GetDeviceIdConnectedTo(out string filterId) != 0 || string.IsNullOrEmpty(filterId)) continue;
                    if (enumerator.GetDevice(filterId, out var filter) != 0) continue;

                    var ksIid = IidKsControl;
                    if (filter.Activate(ref ksIid, ClsCtxAll, IntPtr.Zero, out object? ksObj) != 0
                        || ksObj is not IKsControl ks)
                        continue;

                    yield return ks;
                }
            }
            finally
            {
                Release(topology);
            }
        }

        private static bool BelongsTo(IMMDevice endpoint, Guid container)
        {
            if (endpoint.OpenPropertyStore(StgmRead, out var store) != 0) return false;
            var key = PkeyContainerId;
            if (store.GetValue(ref key, out var value) != 0) return false;
            try
            {
                return value.Vt == VtClsid
                    && value.Slot1 != IntPtr.Zero
                    && Marshal.PtrToStructure<Guid>(value.Slot1) == container;
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }

        private static void Release(object? comObject)
        {
            try
            {
                if (comObject != null && Marshal.IsComObject(comObject))
                    Marshal.ReleaseComObject(comObject);
            }
            catch
            {
                // Nothing useful to do if the release itself fails.
            }
        }
    }
}

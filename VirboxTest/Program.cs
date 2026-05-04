using System;
using System.Runtime.InteropServices;
using System.Text;
using Newtonsoft.Json.Linq;

class Program
{
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    delegate uint ClientOpenDelegate(ref IntPtr ipc);
    delegate uint ClientCloseDelegate(IntPtr ipc);
    delegate uint GetAllDescriptionDelegate(IntPtr ipc, uint format, ref IntPtr desc);
    delegate uint GetLicenseIdDelegate(IntPtr ipc, uint format, string desc, ref IntPtr result);
    delegate uint GetDeviceInfoDelegate(IntPtr ipc, string desc, ref IntPtr result);
    delegate void FreeDelegate(IntPtr buffer);

    static T GetDelegate<T>(IntPtr hModule, string name) where T : Delegate
    {
        IntPtr p = GetProcAddress(hModule, name);
        return p == IntPtr.Zero ? null : (T)Marshal.GetDelegateForFunctionPointer(p, typeof(T));
    }

    static void Main(string[] args)
    {
        string dllPath = "slm_control.dll";
        IntPtr hModule = LoadLibrary(dllPath);
        if (hModule == IntPtr.Zero)
        {
            dllPath = @"C:\dlcv\bin\slm_control.dll";
            hModule = LoadLibrary(dllPath);
        }
        if (hModule == IntPtr.Zero)
        {
            Console.WriteLine("FAIL: Cannot load slm_control.dll");
            return;
        }

        var clientOpen = GetDelegate<ClientOpenDelegate>(hModule, "slm_ctrl_client_open");
        var clientClose = GetDelegate<ClientCloseDelegate>(hModule, "slm_ctrl_client_close");
        var getAllDescription = GetDelegate<GetAllDescriptionDelegate>(hModule, "slm_ctrl_get_all_description");
        var getLicenseId = GetDelegate<GetLicenseIdDelegate>(hModule, "slm_ctrl_get_license_id");
        var getDeviceInfo = GetDelegate<GetDeviceInfoDelegate>(hModule, "slm_ctrl_get_device_info");
        var freeBuffer = GetDelegate<FreeDelegate>(hModule, "slm_ctrl_free");

        if (clientOpen == null || clientClose == null || getAllDescription == null ||
            getLicenseId == null || getDeviceInfo == null || freeBuffer == null)
        {
            Console.WriteLine("FAIL: Cannot get all proc addresses");
            return;
        }

        IntPtr ipc = IntPtr.Zero;
        uint st = clientOpen(ref ipc);
        if (st != 0)
        {
            Console.WriteLine($"FAIL: client_open returned {st}");
            return;
        }

        Console.WriteLine("=== Virbox API Test ===");
        Console.WriteLine();

        // 1. get_all_description
        IntPtr descPtr = IntPtr.Zero;
        st = getAllDescription(ipc, 2, ref descPtr);
        if (st != 0)
        {
            Console.WriteLine($"FAIL: get_all_description returned {st}");
            clientClose(ipc);
            return;
        }

        string descJson = descPtr == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(descPtr);
        freeBuffer(descPtr);
        Console.WriteLine("--- slm_ctrl_get_all_description raw JSON ---");
        Console.WriteLine(descJson);
        Console.WriteLine();

        JToken root;
        try { root = JToken.Parse(descJson); }
        catch (Exception ex)
        {
            Console.WriteLine($"FAIL: parse desc JSON error: {ex.Message}");
            clientClose(ipc);
            return;
        }

        var descList = new System.Collections.Generic.List<JObject>();
        if (root is JObject ro) descList.Add(ro);
        else if (root is JArray ra)
        {
            foreach (var item in ra)
                if (item is JObject jo) descList.Add(jo);
        }

        for (int i = 0; i < descList.Count; i++)
        {
            string descText = descList[i].ToString(Newtonsoft.Json.Formatting.None);
            Console.WriteLine($"=== Description [{i}] ===");
            Console.WriteLine(descText);
            Console.WriteLine();

            // 2. get_device_info
            IntPtr devPtr = IntPtr.Zero;
            st = getDeviceInfo(ipc, descText, ref devPtr);
            if (st == 0 && devPtr != IntPtr.Zero)
            {
                string devJson = Marshal.PtrToStringAnsi(devPtr);
                freeBuffer(devPtr);
                Console.WriteLine($"--- slm_ctrl_get_device_info for desc[{i}] ---");
                Console.WriteLine(devJson);
                Console.WriteLine();
            }
            else
            {
                Console.WriteLine($"--- slm_ctrl_get_device_info for desc[{i}] returned {st} ---");
                Console.WriteLine();
            }

            // 3. get_license_id
            IntPtr licPtr = IntPtr.Zero;
            st = getLicenseId(ipc, 2, descText, ref licPtr);
            if (st == 0 && licPtr != IntPtr.Zero)
            {
                string licJson = Marshal.PtrToStringAnsi(licPtr);
                freeBuffer(licPtr);
                Console.WriteLine($"--- slm_ctrl_get_license_id for desc[{i}] ---");
                Console.WriteLine(licJson);
                Console.WriteLine();
            }
            else
            {
                Console.WriteLine($"--- slm_ctrl_get_license_id for desc[{i}] returned {st} ---");
                Console.WriteLine();
            }
        }

        clientClose(ipc);
        Console.WriteLine("=== Test Finished ===");
    }
}

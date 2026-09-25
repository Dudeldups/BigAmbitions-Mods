#nullable enable
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BigHax
{
    /// <summary>
    /// Small Windows x64 method-entry detour used for game helper methods whose
    /// return values cannot be changed through public data or events.
    /// </summary>
    internal sealed class BigHaxMethodDetour
    {
        private const uint PageExecuteReadWrite = 0x40;
        private const int JumpSize = 14;

        private readonly MethodInfo target;
        private readonly MethodInfo replacement;
        private byte[]? originalBytes;
        private byte[]? jumpBytes;
        private IntPtr targetAddress;

        public BigHaxMethodDetour(MethodInfo target, MethodInfo replacement)
        {
            this.target = target ?? throw new ArgumentNullException(nameof(target));
            this.replacement = replacement ?? throw new ArgumentNullException(nameof(replacement));
        }

        public bool IsApplied { get; private set; }

        public bool Apply(out string error)
        {
            error = string.Empty;
            if (IsApplied)
                return true;

            if (Environment.OSVersion.Platform != PlatformID.Win32NT || IntPtr.Size != 8)
            {
                error = "method detours require the Windows x64 Mono runtime";
                return false;
            }

            try
            {
                RuntimeHelpers.PrepareMethod(target.MethodHandle);
                RuntimeHelpers.PrepareMethod(replacement.MethodHandle);
                targetAddress = target.MethodHandle.GetFunctionPointer();
                var replacementAddress = replacement.MethodHandle.GetFunctionPointer();

                originalBytes = new byte[JumpSize];
                Marshal.Copy(targetAddress, originalBytes, 0, originalBytes.Length);
                jumpBytes = CreateAbsoluteJump(replacementAddress);
                WriteBytes(targetAddress, jumpBytes);
                IsApplied = true;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.ToString();
                return false;
            }
        }

        public bool Restore(out string error)
        {
            error = string.Empty;
            if (!IsApplied || originalBytes == null || jumpBytes == null)
                return true;

            try
            {
                var currentBytes = new byte[JumpSize];
                Marshal.Copy(targetAddress, currentBytes, 0, currentBytes.Length);
                if (!BytesEqual(currentBytes, jumpBytes))
                {
                    error = "target entry was changed after Big Hax installed its detour";
                    return false;
                }

                WriteBytes(targetAddress, originalBytes);
                IsApplied = false;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.ToString();
                return false;
            }
        }

        private static byte[] CreateAbsoluteJump(IntPtr destination)
        {
            // jmp qword ptr [rip+0] followed by the absolute 64-bit address.
            var bytes = new byte[JumpSize];
            bytes[0] = 0xFF;
            bytes[1] = 0x25;
            var addressBytes = BitConverter.GetBytes(destination.ToInt64());
            Buffer.BlockCopy(addressBytes, 0, bytes, 6, addressBytes.Length);
            return bytes;
        }

        private static void WriteBytes(IntPtr address, byte[] bytes)
        {
            if (!VirtualProtect(address, new UIntPtr((uint)bytes.Length), PageExecuteReadWrite, out var previousProtection))
                throw new InvalidOperationException("VirtualProtect failed before writing a method detour.");

            try
            {
                Marshal.Copy(bytes, 0, address, bytes.Length);
                FlushInstructionCache(GetCurrentProcess(), address, new UIntPtr((uint)bytes.Length));
            }
            finally
            {
                VirtualProtect(address, new UIntPtr((uint)bytes.Length), previousProtection, out _);
            }
        }

        private static bool BytesEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length)
                return false;

            for (var index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index])
                    return false;
            }

            return true;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtect(IntPtr address, UIntPtr size, uint newProtection, out uint oldProtection);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FlushInstructionCache(IntPtr process, IntPtr baseAddress, UIntPtr size);
    }
}

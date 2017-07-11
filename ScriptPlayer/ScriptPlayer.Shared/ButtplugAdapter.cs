﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Buttplug.Client;
using Buttplug.Core.Messages;

namespace ScriptPlayer.Shared
{
    public class ButtplugAdapter : IDeviceController
    {
        public event EventHandler<string> DeviceAdded;

        private ButtplugWSClient _client;
        private readonly string _url;

        private List<ButtplugClientDevice> _devices;

        public ButtplugAdapter(string url = "ws://localhost:12345/buttplug")
        {
            _devices = new List<ButtplugClientDevice>();
            _url = url;
        }

        private void Client_DeviceAddedOrRemoved(object sender, DeviceEventArgs deviceEventArgs)
        {
            var device = DirtyHacks.GetPrivateField<ButtplugClientDevice>(deviceEventArgs, "device");
            var action = DirtyHacks.GetPrivateField<DeviceEventArgs.DeviceAction>(deviceEventArgs, "action");

            switch (action)
            {
                case DeviceEventArgs.DeviceAction.ADDED:
                    _devices.Add(device);
                    OnDeviceAdded(device.Name);
                    break;
                case DeviceEventArgs.DeviceAction.REMOVED:
                    _devices.RemoveAll(dev => dev.Index == device.Index);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public async Task<bool> Connect()
        {
            try
            {
                _client = new ButtplugWSClient("ScriptPlayer");
                _client.DeviceAdded += Client_DeviceAddedOrRemoved;
                _client.DeviceRemoved += Client_DeviceAddedOrRemoved;

                await _client.Connect(new Uri(_url));
                _devices = (await GetDeviceList()).ToList();

                return true;
            }
            catch
            {
                _client = null;
                return false;
            }
        }

        private async Task<IEnumerable<ButtplugClientDevice>> GetDeviceList()
        {
            try
            {
                //TODO Still not working?
                await _client.RequestDeviceList();
                //await Task.Delay(2000);
                return _client.getDevices();
            }
            catch
            {
                return new List<ButtplugClientDevice>();
            }
        }

        public async void Set(DeviceCommandInformation information)
        {
            if (_client == null) return;

            var devices = _devices;
            foreach (ButtplugClientDevice device in devices)
            {
                if (device.AllowedMessages.Contains(nameof(FleshlightLaunchFW12Cmd)))
                {
                    await _client.SendDeviceMessage(device, new FleshlightLaunchFW12Cmd(device.Index, information.SpeedTransformed, information.PositionToTransformed));
                }
                else if (device.AllowedMessages.Contains(nameof(KiirooCmd)))
                {
                    await _client.SendDeviceMessage(device, new KiirooCmd(device.Index, LaunchToKiiroo(information.PositionToOriginal, 0, 4)));
                }
                else if (device.AllowedMessages.Contains(nameof(SingleMotorVibrateCmd)))
                {
                    await _client.SendDeviceMessage(device, new SingleMotorVibrateCmd(device.Index, LaunchToVibrator(information.SpeedOriginal)));

                    Task.Run(new Action(async () =>
                    {
                        TimeSpan duration = TimeSpan.FromMilliseconds(Math.Min(1000, information.Duration.TotalMilliseconds / 2.0));
                        await Task.Delay(duration);
                        await _client.SendDeviceMessage(device, new SingleMotorVibrateCmd(device.Index, 0));
                    }));
                }
                else if (device.AllowedMessages.Contains(nameof(VorzeA10CycloneCmd)))
                {
                    await _client.SendDeviceMessage(device, new VorzeA10CycloneCmd(device.Index, LaunchToVorze(information.SpeedOriginal), information.PositionToOriginal > information.PositionFromOriginal));
                }
                else if (device.AllowedMessages.Contains(nameof(LovenseCmd)))
                {
                    //await _client.SendDeviceMessage(device, new LovenseCmd(device.Index, LaunchToLovense(position, speed)));
                }
            }
        }

        private uint LaunchToVorze(byte speed)
        {
            return speed;
        }

        private double LaunchToVibrator(byte speed)
        {
            //1.0 doesn't work?
            return Math.Min(0.9999, Math.Max(0.25, (speed+1.0) / 100.0));
        }

        private string LaunchToLovense(byte position, byte speed)
        {
            return "https://github.com/metafetish/lovesense-rs";
        }

        private uint LaunchToKiiroo(byte position, uint min, uint max)
        {
            double pos = position / 0.99;

            uint result = Math.Min(max, Math.Max(min, (uint)Math.Round(pos * (max - min) + min)));

            return result;
        }

        public async Task Disconnect()
        {
            if (_client == null) return;
            _devices.Clear();
            await _client.Disconnect();
        }

        public async Task StartScanning()
        {
            if (_client == null) return;
            await _client.StartScanning();
        }

        protected virtual void OnDeviceAdded(string e)
        {
            DeviceAdded?.Invoke(this, e);
        }
    }
}
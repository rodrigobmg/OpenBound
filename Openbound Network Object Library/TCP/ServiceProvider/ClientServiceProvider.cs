﻿/* 
 * Copyright (C) 2020, Carlos H.M.S. <carlos_judo@hotmail.com>
 * This file is part of OpenBound.
 * OpenBound is free software: you can redistribute it and/or modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of the License, or(at your option) any later version.
 * 
 * OpenBound is distributed in the hope that it will be useful, but WITHOUT ANY WARRANTY; without even the implied warranty
 * of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.See the GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License along with OpenBound. If not, see http://www.gnu.org/licenses/.
 */

using OpenBound_Network_Object_Library.Common;
using OpenBound_Network_Object_Library.Extension;
using OpenBound_Network_Object_Library.TCP.Entity;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace OpenBound_Network_Object_Library.TCP.ServiceProvider
{
    public class ClientServiceProvider
    {
        public ExtendedConcurrentQueue<byte[]> RequestQueue;

        protected string serverAddress;
        protected int serverPort;
        protected int bufferSize;

        protected Action<ClientServiceProvider, string[]> consumerAction;
        
        public Action OnFailToEstabilishConnection;

        protected Thread operationThread;
        protected Thread consumerThread;

        protected NetworkStream stream;

        public ClientServiceProvider(string serverAddress, int serverPort, int bufferSize, Action<ClientServiceProvider, string[]> consumerAction)
        {
            this.serverAddress = serverAddress;
            this.serverPort = serverPort;
            this.bufferSize = bufferSize;
            this.consumerAction = consumerAction;

            RequestQueue = new ExtendedConcurrentQueue<byte[]>();
        }

        ~ClientServiceProvider()
        {
            StopOperation();
        }

        public void StartOperation()
        {
            if (operationThread == null || !operationThread.IsAlive)
                operationThread = new Thread(ClientServerOperationThread);

            operationThread.Start();
        }

        public void StopOperation()
        {
            if (operationThread != null)
                operationThread.Interrupt();
            if (consumerThread != null)
                consumerThread.Interrupt();

            try
            {
                stream.Dispose();
            }
            catch (Exception) { Console.WriteLine($"Connection to {serverAddress}:{serverPort} severed."); }
        }

        void ClientServerOperationThread()
        {
            try
            {
                TcpClient client = new TcpClient();

                client.Connect(serverAddress, serverPort);
                stream = client.GetStream();

                RequestQueue.OnEnqueueAction = (item) =>
                {
                    while (!client.Connected)
                        Thread.Sleep(100);

                    AsyncSendMessage(stream);
                };

                consumerThread = new Thread(ConsumerThread);
                consumerThread.IsBackground = true;

                consumerThread.Start();

                //If the RequestQueue has received values before the connection was made
                AsyncSendMessage(stream);
            }
            catch (ThreadInterruptedException) { }
            catch (Exception ex)
            {
                OnFailToEstabilishConnection?.Invoke();
                Console.WriteLine($"ClientServerOperationThread: {ex.Message}");
            }
        }

        void AsyncSendMessage(NetworkStream stream)
        {
            try
            {
                while (!RequestQueue.IsEmpty)
                {
                    byte[] request = RequestQueue.Dequeue();
                    Array.Resize(ref request, bufferSize);
                    #warning Things may go wrong??
                    stream.WriteAsync(request, 0, bufferSize);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"AsyncSendMessage: {ex.Message}");
            }
        }

        void ConsumerThread()
        {
            try
            {
                byte[] response = new byte[bufferSize];

                while (true)
                {
                    int received = 0;

                    while (received < bufferSize)
                        received += stream.Read(response, received, bufferSize - received);

                    string[] message = ObjectWrapper.ConvertByteArrayToObject<string>(response).Split('|');

                    consumerAction(this, message);
                }
            }
            catch (IOException) { /* socket was already disposed */ }
            catch (Exception ex)
            {
                Console.WriteLine($"ConsumerThread: {ex.Message}");
            }
            finally
            {
                try
                {
                    stream.Dispose();
                }
                catch (Exception) { }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using XInputDotNetPure;
using ButtonState = XInputDotNetPure.ButtonState;

namespace WinLaunch
{
    partial class MainWindow : Window
    {
        Thread gamepadInputThread;
        CancellationTokenSource gamepadInputCancellation;
        float gamepadThreshold = 0.9f;

        class GamePadInputState
        {
            public bool gamepadToggleLaunchpadSent = false;

            public bool gamepadStickInputSent = false;
            public bool gamepadEnterInputSent = false;

            public DispatcherTimer delayTimer = new DispatcherTimer();
            public DispatcherTimer repeatTimer = new DispatcherTimer();

            public DispatcherTimer aPressTimer = new DispatcherTimer();

            public GamePadInputState()
            {
                delayTimer.Interval = TimeSpan.FromMilliseconds(800);
                delayTimer.Tick += RepeatTimer_Tick;

                repeatTimer.Interval = TimeSpan.FromMilliseconds(100);
                repeatTimer.Tick += ResetTimer_Tick;

                aPressTimer.Interval = TimeSpan.FromMilliseconds(300);
                aPressTimer.Tick += APressTimer_Tick;
            }

            private void APressTimer_Tick(object sender, EventArgs e)
            {
                aPressTimer.Stop();
            }

            private void RepeatTimer_Tick(object sender, EventArgs e)
            {
                repeatTimer.Start();
                delayTimer.Stop();
            }

            private void ResetTimer_Tick(object sender, EventArgs e)
            {
                gamepadStickInputSent = false;
            }
        }

        Dictionary<PlayerIndex, GamePadInputState> inputStates;

        public void InitGamepadInput()
        {
            inputStates = new Dictionary<PlayerIndex, GamePadInputState>();
            inputStates[PlayerIndex.One] = new GamePadInputState();
            inputStates[PlayerIndex.Two] = new GamePadInputState();
            inputStates[PlayerIndex.Three] = new GamePadInputState();
            inputStates[PlayerIndex.Four] = new GamePadInputState();
        }

        public void StartGamepadInput()
        {
            StopGamepadInput();

            gamepadInputCancellation = new CancellationTokenSource();

            gamepadInputThread = new Thread(new ParameterizedThreadStart(GamepadLoop));
            gamepadInputThread.IsBackground = true;
            gamepadInputThread.Start(gamepadInputCancellation.Token);
        }

        public void StopGamepadInput()
        {
            if (gamepadInputCancellation == null)
                return;

            gamepadInputCancellation.Cancel();

            //the loop wakes up every 20ms, so this returns quickly
            if (gamepadInputThread != null)
                gamepadInputThread.Join(TimeSpan.FromSeconds(1));

            gamepadInputCancellation.Dispose();
            gamepadInputCancellation = null;
            gamepadInputThread = null;
        }

        private void ProcessInputForPlayer(PlayerIndex index)
        {
            var state = GamePad.GetState(index);

            if (!state.IsConnected)
                return;

            if (Settings.CurrentSettings.GamepadActivation)
            {
                if (state.Buttons.Start == ButtonState.Pressed && ActivatorsEnabled)
                {
                    if (!inputStates[index].gamepadToggleLaunchpadSent)
                    {
                        Dispatcher.BeginInvoke(new Action(() =>
                        {
                            DebugLog.WriteToLogFile("Gamepad activated");

                            ToggleLaunchpad();
                        }));

                        inputStates[index].gamepadToggleLaunchpadSent = true;
                    }
                }
                else
                {
                    inputStates[index].gamepadToggleLaunchpadSent = false;
                }
            }

            if (Visibility == Visibility.Visible)
            {
                if (state.DPad.Right == ButtonState.Pressed || state.ThumbSticks.Left.X > gamepadThreshold)
                {
                    //right
                    SendGamepadInput(Key.Right, index);
                }
                else if (state.DPad.Left == ButtonState.Pressed || state.ThumbSticks.Left.X < -gamepadThreshold)
                {
                    //left
                    SendGamepadInput(Key.Left, index);
                }
                else if (state.DPad.Up == ButtonState.Pressed || state.ThumbSticks.Left.Y > gamepadThreshold)
                {
                    //up
                    SendGamepadInput(Key.Up, index);
                }
                else if (state.DPad.Down == ButtonState.Pressed || state.ThumbSticks.Left.Y < -gamepadThreshold)
                {
                    //down
                    SendGamepadInput(Key.Down, index);
                }
                else
                {
                    //deadzone
                    inputStates[index].gamepadStickInputSent = false;
                    inputStates[index].delayTimer.Stop();
                    inputStates[index].repeatTimer.Stop();
                }

                if (state.Buttons.A == ButtonState.Pressed)
                {
                    if (!inputStates[index].aPressTimer.IsEnabled)
                    {
                        SendGamepadInput(Key.Enter, index);
                    }
                }
                else if (state.Buttons.A == ButtonState.Released)
                {
                    inputStates[index].gamepadEnterInputSent = false;
                }
            }
        }

        private void GamepadLoop(object state)
        {
            CancellationToken token = (CancellationToken)state;

            while (!token.IsCancellationRequested)
            {
                ProcessInputForPlayer(PlayerIndex.One);
                ProcessInputForPlayer(PlayerIndex.Two);
                ProcessInputForPlayer(PlayerIndex.Three);
                ProcessInputForPlayer(PlayerIndex.Four);

                token.WaitHandle.WaitOne(20);
            }
        }

        private void SendGamepadInput(Key key, PlayerIndex index)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    if (key != Key.Enter && inputStates[index].gamepadStickInputSent)
                        return;

                    if (key == Key.Enter && inputStates[index].gamepadEnterInputSent)
                        return;

                    if (Keyboard.PrimaryDevice != null)
                    {
                        if (Keyboard.PrimaryDevice.ActiveSource != null)
                        {
                            if(SBM != null)
                                SBM.KeyDown(this, new System.Windows.Input.KeyEventArgs(Keyboard.PrimaryDevice, Keyboard.PrimaryDevice.ActiveSource, 0, key) { RoutedEvent = Keyboard.KeyDownEvent });

                            if (key == Key.Enter)
                            {
                                inputStates[index].gamepadEnterInputSent = true;
                                inputStates[index].aPressTimer.Start();
                            }
                            else
                            {
                                inputStates[index].gamepadStickInputSent = true;

                                if (!inputStates[index].repeatTimer.IsEnabled)
                                {
                                    inputStates[index].delayTimer.Start();
                                }
                            }
                        }
                    }
                }
                catch { }
            }));
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-1000)]
public class Game : MonoBehaviour
{
    public static Game Instance { get; private set; }

    public interface IGameLoop
    {
        bool Init(string[] args);
        void Update();
        void FixedUpdate();
        void LateUpdate();
        void Shutdown();
    }

    private void Awake() {
        Instance = this;
    }

    private void Update()
    {
        if (_requestedGameLoopTypes.Count > 0) {
            // Multiple running gameloops only allowed in editor
#if !UNITY_EDITOR
            ShutdownGameLoops();
#endif
            bool initSucceeded = true;
            for (int i = 0; i < _requestedGameLoopTypes.Count; i++) {
                try {
                    IGameLoop gameLoop = (IGameLoop)System.Activator.CreateInstance(_requestedGameLoopTypes[i]);
                    initSucceeded = gameLoop.Init(_requestedGameLoopArgs[i]);
                    if (!initSucceeded)
                        break;

                    _gameLoops.Add(gameLoop);
                }catch(System.Exception e) {
                    GameDebug.Log(string.Format("Game loop initialization threw exception : ({0})\n{1}", e.Message, e.StackTrace));
                }
            }

            if (!initSucceeded) {
                ShutdownGameLoops();

                GameDebug.Log("Game loop initialization failed ... reverting to boot loop");
            }
        }

        try {
            if (!_errorState) {
                foreach (var gameLoop in _gameLoops) {
                    gameLoop.Update();
                }
            }
        } catch(System.Exception e) {
            HandleGameloopException(e);
        }

        _requestedGameLoopArgs.Clear();
        _requestedGameLoopTypes.Clear();
    }

    private void FixedUpdate() {
        try {
            if (!_errorState) {
                foreach (var gameLoop in _gameLoops) {
                    gameLoop.FixedUpdate();
                }
            }
        } catch (System.Exception e) {
            HandleGameloopException(e);
        }
    }

    private void LateUpdate() {
        try {
            if (!_errorState) {
                foreach (var gameLoop in _gameLoops) {
                    gameLoop.LateUpdate();
                }
            }
        } catch (System.Exception e) {
            HandleGameloopException(e);
        }
    }

    void HandleGameloopException(System.Exception e) {
        GameDebug.Log("EXCEPTION " + e.Message + "\n" + e.StackTrace);
        Console.SetOpen(true);
        _errorState = true;
    }

    private void ShutdownGameLoops() {
        foreach (var gameLoop in _gameLoops) {
            gameLoop.Shutdown();
        }
    }

    private bool _errorState;

    private List<IGameLoop> _gameLoops = new List<IGameLoop>();
    private List<System.Type> _requestedGameLoopTypes = new List<System.Type>();
    private List<string[]> _requestedGameLoopArgs = new List<string[]>();
}

using System;
using System.Collections.Generic;
using TGS.Spine42;
using TGS.Spine42.Unity;
using ugf.utils;
using UnityEngine;

namespace com.tgs.spinehandler
{
    /// <summary>
    /// Helper principal para manipulação de animações Spine.
    /// Fornece métodos para reproduzir animações, pular para eventos/tempos específicos,
    /// e obter informações sobre eventos da timeline.
    /// </summary>
    public class SpineHandler : MonoBehaviour
    {
        [Header("Spine Configuration")]
        [Tooltip("Referência ao componente SkeletonAnimation")]
        [SerializeField] private Spine42_SkeletonAnimation _skeletonAnimation;

        private Dictionary<string, SpineAnimationData> _animationsDataDict;
        private DelayCalls _delayCalls;

        /// <summary>
        /// Armazena a referência do onFinishAction pendente por track.
        /// Permite cancelar o callback quando um JumpTo é executado.
        /// </summary>
        private Dictionary<int, Action> _pendingFinishCallbacks;


        private void Awake()
        {
            _delayCalls = gameObject.AddComponent<DelayCalls>();
            _pendingFinishCallbacks = new Dictionary<int, Action>();
        }

        private void Start()
        {
            Initialize();
        }

        /// <summary>
        /// Inicializa o SpineHandler mapeando todos os eventos das animações.
        /// </summary>
        private void Initialize()
        {
            if (!ValidateSkeletonAnimation())
                return;

            MapEventTimes();
        }

        private void OnDestroy()
        {
            if (_delayCalls != null)
                Destroy(_delayCalls);
        }

        /// <summary>
        /// Valida se o SkeletonAnimation está atribuído.
        /// </summary>
        private bool ValidateSkeletonAnimation()
        {
            if (_skeletonAnimation == null)
            {
                Debug.LogError($"[SpineHandler] SkeletonAnimation não atribuído em '{gameObject.name}'", this);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Mapeia todos os eventos de todas as animações do skeleton.
        /// </summary>
        private void MapEventTimes()
        {
            _animationsDataDict = new Dictionary<string, SpineAnimationData>();

            foreach (Spine42_Animation anim in _skeletonAnimation.skeleton.Data.Animations)
            {
                SpineAnimationData animationData = new SpineAnimationData(anim);

                foreach (var timeline in anim.Timelines)
                {
                    if (timeline is EventTimeline eventTimeline)
                    {
                        for (int i = 0; i < eventTimeline.Events.Length; i++)
                        {
                            string eventName = eventTimeline.Events[i].Data.Name;
                            float eventTime = eventTimeline.Frames[i];

                            animationData.AddEvent(eventName, eventTime);
                        }
                    }
                }

                _animationsDataDict[anim.Name] = animationData;
            }
        }

        #region PLAY ANIMATION
        /// <summary>
        /// Reproduz uma animação com opções de tempo inicial, callback em tempo específico e loop.
        /// ATENÇÃO: Não misture múltiplos parâmetros do mesmo tipo:
        /// - Use APENAS UM de: startAtTime, startAtPercentage, startAtEvent
        /// - Use APENAS UM de: onReachTime, onReachPercentage, onReachEvent
        /// Caso contrário, será logado um warning e a precedência será aplicada.
        /// </summary>
        /// <param name="animationName">Nome da animação a ser reproduzida</param>
        /// <param name="startAtTime">Tempo ABSOLUTO inicial da animação (em segundos). Default: 0</param>
        /// <param name="startAtPercentage">Porcentagem para iniciar (0-1). Ignorado se startAtTime > 0. Default: 0</param>
        /// <param name="startAtEvent">Evento para iniciar. Ignorado se startAtTime > 0 ou startAtPercentage > 0. Default: null</param>
        /// <param name="onReachTime">Tempo ABSOLUTO na animação para callback (em segundos). Default: 0</param>
        /// <param name="onReachPercentage">Porcentagem (0-1) para callback. Ignorado se onReachTime > 0. Default: 0</param>
        /// <param name="onReachEvent">Evento para callback. Ignorado se onReachTime > 0 ou onReachPercentage > 0. Default: null</param>
        /// <param name="onReachAction">Ação a ser executada quando atingir o tempo/porcentagem/evento especificado</param>
        /// <param name="timeActions">Lista de tuplas (tempo, ação) para agendar múltiplas ações em tempos absolutos. Default: null</param>
        /// <param name="percentageActions">Lista de tuplas (porcentagem, ação) para agendar múltiplas ações em porcentagens da animação. Default: null</param>
        /// <param name="eventActions">Lista de tuplas (nomeEvento, ação) para agendar múltiplas ações em diferentes eventos. Default: null</param>
        /// <param name="onFinishAction">Ação a ser executada quando a animação terminar (considerando startAt)</param>
        /// <param name="trackIndex">Índice da track de animação. Default: 0</param>
        /// <param name="playTimes">Quantidade de vezes para tocar. 1 = toca 1x (default), 0 = não toca, < 0 = loop infinito, > 1 = toca X vezes.</param>
        public void PlayAnimation(
            string animationName,
            float startAtTime = 0.0f,
            float startAtPercentage = 0.0f,
            string startAtEvent = null,
            float onReachTime = 0.0f,
            float onReachPercentage = 0.0f,
            string onReachEvent = null,
            Action onReachAction = null,
            List<(string eventName, Action action)> eventActions = null,
            List<(float time, Action action)> timeActions = null,
            List<(float percentage, Action action)> percentageActions = null,
            Action onFinishAction = null,
            int trackIndex = 0,
            int playTimes = 1)
        {
            //* Validações
            if (!ValidateSkeletonAnimation())
            {
                return;
            }

            //* playTimes == 0 significa "não tocar"
            if (playTimes == 0)
            {
                return;
            }

            Spine42_Animation anim = _skeletonAnimation.Skeleton.Data.FindAnimation(animationName);
            if (anim == null)
            {
                Debug.LogWarning($"[SpineHandler] Animação '{animationName}' não encontrada em '{gameObject.name}'", this);
                return;
            }

            //* Warnings para uso de múltiplos parâmetros conflitantes
            int startAtCount = (startAtTime > 0 ? 1 : 0) + (startAtPercentage > 0 ? 1 : 0) + (!string.IsNullOrEmpty(startAtEvent) ? 1 : 0);
            if (startAtCount > 1)
            {
                Debug.LogWarning($"[SpineHandler] Múltiplos parâmetros 'startAt' fornecidos. Precedência: startAtTime > startAtPercentage > startAtEvent em '{gameObject.name}'", this);
            }

            int onReachCount = (onReachTime > 0 ? 1 : 0) + (onReachPercentage > 0 ? 1 : 0) + (!string.IsNullOrEmpty(onReachEvent) ? 1 : 0);
            if (onReachCount > 1)
            {
                Debug.LogWarning($"[SpineHandler] Múltiplos parâmetros 'onReach' fornecidos. Precedência: onReachTime > onReachPercentage > onReachEvent em '{gameObject.name}'", this);
            }

            //* Configura loop infinito se playTimes < 0
            bool isInfiniteLoop = playTimes < 0;

            //* Reproduz a animação
            _skeletonAnimation.AnimationState.SetAnimation(trackIndex, anim, isInfiniteLoop);

            //* Se playTimes > 1, enfileira as repetições adicionais
            if (playTimes > 1)
            {
                for (int i = 1; i < playTimes; i++)
                {
                    _skeletonAnimation.AnimationState.AddAnimation(trackIndex, anim, false, 0);
                }
            }

            float startTime = 0.0f;

            //* Determinar tempo inicial (precedência: Time > Percentage > Event)
            if (startAtTime > 0)
            {
                startTime = startAtTime;
                JumpToTime(startAtTime, trackIndex);
            }
            else if (startAtPercentage > 0)
            {
                startTime = anim.Duration * startAtPercentage;
                JumpToPercentage(startAtPercentage, trackIndex);
            }
            else if (!string.IsNullOrEmpty(startAtEvent))
            {
                if (TryGetEventTime(animationName, startAtEvent, out float eventTime))
                {
                    startTime = eventTime;
                    JumpToEvent(startAtEvent, trackIndex);
                }
                else
                {
                    Debug.LogError($"[SpineHandler] Evento '{startAtEvent}' não encontrado na animação '{animationName}' em '{gameObject.name}'", this);
                }
            }

            //* Determinar e agendar callback (precedência: Time > Percentage > Event)
            if (onReachAction != null)
            {
                if (onReachTime > 0)
                {
                    // onReachTime é ABSOLUTO na timeline da animação
                    float deltaTime = onReachTime - startTime;
                    if (deltaTime > 0)
                    {
                        _delayCalls.DelayedCall(onReachAction, deltaTime);
                    }
                    else
                    {
                        Debug.LogWarning($"[SpineHandler] onReachTime ({onReachTime:F2}s) é menor ou igual ao startTime ({startTime:F2}s). Callback não será executado em '{gameObject.name}'", this);
                    }
                }
                else if (onReachPercentage > 0)
                {
                    // onReachPercentage é relativo à duração TOTAL da animação
                    float targetTime = anim.Duration * onReachPercentage;
                    float deltaTime = targetTime - startTime;
                    if (deltaTime > 0)
                    {
                        _delayCalls.DelayedCall(onReachAction, deltaTime);
                    }
                    else
                    {
                        Debug.LogWarning($"[SpineHandler] onReachPercentage ({onReachPercentage:P0}) resulta em tempo ({targetTime:F2}s) menor ou igual ao startTime ({startTime:F2}s). Callback não será executado em '{gameObject.name}'", this);
                    }
                }
                else if (!string.IsNullOrEmpty(onReachEvent))
                {
                    if (TryGetEventTime(animationName, onReachEvent, out float eventTime))
                    {
                        float deltaTime = eventTime - startTime;
                        if (deltaTime > 0)
                        {
                            _delayCalls.DelayedCall(onReachAction, deltaTime);
                        }
                        else
                        {
                            Debug.LogWarning($"[SpineHandler] onReachEvent '{onReachEvent}' ({eventTime:F2}s) é menor ou igual ao startTime ({startTime:F2}s). Callback não será executado em '{gameObject.name}'", this);
                        }
                    }
                    else
                    {
                        Debug.LogError($"[SpineHandler] Evento '{onReachEvent}' não encontrado na animação '{animationName}' em '{gameObject.name}'", this);
                    }
                }
            }

            //* Processar lista de timeActions
            if (timeActions != null && timeActions.Count > 0)
            {
                foreach (var (time, action) in timeActions)
                {
                    if (action == null) continue;

                    float deltaTime = time - startTime;
                    if (deltaTime > 0)
                    {
                        _delayCalls.DelayedCall(action, deltaTime);
                    }
                    else
                    {
                        Debug.LogWarning($"[SpineHandler] timeAction ({time:F2}s) é menor ou igual ao startTime ({startTime:F2}s). Callback não será executado em '{gameObject.name}'", this);
                    }
                }
            }

            //* Processar lista de percentageActions
            if (percentageActions != null && percentageActions.Count > 0)
            {
                foreach (var (percentage, action) in percentageActions)
                {
                    if (action == null) continue;

                    float targetTime = anim.Duration * percentage;
                    float deltaTime = targetTime - startTime;
                    if (deltaTime > 0)
                    {
                        _delayCalls.DelayedCall(action, deltaTime);
                    }
                    else
                    {
                        Debug.LogWarning($"[SpineHandler] percentageAction ({percentage:P0}) resulta em tempo ({targetTime:F2}s) menor ou igual ao startTime ({startTime:F2}s). Callback não será executado em '{gameObject.name}'", this);
                    }
                }
            }

            //* Processar lista de eventActions
            if (eventActions != null && eventActions.Count > 0)
            {
                foreach (var (eventName, action) in eventActions)
                {
                    if (action == null) continue;

                    if (TryGetEventTime(animationName, eventName, out float eventTime))
                    {
                        float deltaTime = eventTime - startTime;
                        if (deltaTime > 0)
                        {
                            _delayCalls.DelayedCall(action, deltaTime);
                        }
                        else
                        {
                            Debug.LogWarning($"[SpineHandler] Evento '{eventName}' ({eventTime:F2}s) é menor ou igual ao startTime ({startTime:F2}s). Callback não será executado em '{gameObject.name}'", this);
                        }
                    }
                    else
                    {
                        Debug.LogError($"[SpineHandler] Evento '{eventName}' não encontrado na animação '{animationName}' em '{gameObject.name}'", this);
                    }
                }
            }

            //* Callback ao finalizar a animação (considerando tempo inicial e repetições finitas)
            ScheduleFinishCallback(trackIndex, anim.Duration, startTime, playTimes, onFinishAction);
        }
        #endregion

        #region JUMP TO
        /// <summary>
        /// Pula para um tempo específico na animação atual.
        /// Cancela automaticamente qualquer onFinishAction pendente na track.
        /// </summary>
        /// <param name="time">Tempo em segundos</param>
        /// <param name="trackIndex">Índice da track de animação. Default: 0</param>
        /// <param name="eventActions">Lista de tuplas (nomeEvento, ação) para agendar múltiplas ações em diferentes eventos após o pulo. Default: null</param>
        /// <param name="onFinishAction">Novo callback a ser executado quando a animação terminar. Se null, nenhum callback será agendado.</param>
        /// <returns>True se o salto foi realizado com sucesso</returns>
        public bool JumpToTime(float time, int trackIndex = 0, List<(string eventName, Action action)> eventActions = null, Action onFinishAction = null)
        {
            if (time == 0)
                return true;

            if (!ValidateSkeletonAnimation())
                return false;

            TrackEntry currentTrackEntry = _skeletonAnimation.AnimationState.GetCurrent(trackIndex);

            if (currentTrackEntry == null)
            {
                Debug.LogError($"[SpineHandler] TrackEntry não encontrada no índice {trackIndex} em '{gameObject.name}'", this);
                return false;
            }

            if (time < 0 || time > currentTrackEntry.Animation.Duration)
            {
                Debug.LogError($"[SpineHandler] Tempo {time:F2}s inválido. Duração da animação '{currentTrackEntry.Animation.Name}': {currentTrackEntry.Animation.Duration:F2}s em '{gameObject.name}'", this);
                return false;
            }

            currentTrackEntry.TrackTime = time;

            // Cancela o callback pendente e agenda o novo (se fornecido)
            CancelPendingFinishCallback(trackIndex);
            ScheduleEventActions(currentTrackEntry.Animation.Name, time, eventActions);
            ScheduleFinishCallback(trackIndex, currentTrackEntry.Animation.Duration, time, 0, onFinishAction);

            return true;
        }

        /// <summary>
        /// Pula para uma porcentagem específica da animação atual.
        /// Cancela automaticamente qualquer onFinishAction pendente na track.
        /// </summary>
        /// <param name="percentage">Porcentagem da duração (0-1)</param>
        /// <param name="trackIndex">Índice da track de animação. Default: 0</param>
        /// <param name="eventActions">Lista de tuplas (nomeEvento, ação) para agendar múltiplas ações em diferentes eventos após o pulo. Default: null</param>
        /// <param name="onFinishAction">Novo callback a ser executado quando a animação terminar. Se null, nenhum callback será agendado.</param>
        /// <returns>True se o salto foi realizado com sucesso</returns>
        public bool JumpToPercentage(float percentage, int trackIndex = 0, List<(string eventName, Action action)> eventActions = null, Action onFinishAction = null)
        {
            if (percentage <= 0)
                return true;

            if (!ValidateSkeletonAnimation())
                return false;

            TrackEntry currentTrackEntry = _skeletonAnimation.AnimationState.GetCurrent(trackIndex);

            if (currentTrackEntry == null)
            {
                Debug.LogError($"[SpineHandler] TrackEntry não encontrada no índice {trackIndex} em '{gameObject.name}'", this);
                return false;
            }

            if (percentage < 0 || percentage > 1)
            {
                Debug.LogError($"[SpineHandler] Porcentagem {percentage:P2} inválida em '{gameObject.name}'", this);
                return false;
            }

            float targetTime = currentTrackEntry.Animation.Duration * percentage;
            currentTrackEntry.TrackTime = targetTime;

            // Cancela o callback pendente e agenda o novo (se fornecido)
            CancelPendingFinishCallback(trackIndex);
            ScheduleEventActions(currentTrackEntry.Animation.Name, targetTime, eventActions);
            ScheduleFinishCallback(trackIndex, currentTrackEntry.Animation.Duration, targetTime, 0, onFinishAction);

            return true;
        }

        /// <summary>
        /// Pula para um evento específico na timeline. A animação com o evento deve estar em execução.
        /// Cancela automaticamente qualquer onFinishAction pendente na track.
        /// </summary>
        /// <param name="eventName">Nome do evento alvo</param>
        /// <param name="trackIndex">Índice da track de animação. Default: 0</param>
        /// <param name="eventActions">Lista de tuplas (nomeEvento, ação) para agendar múltiplas ações em diferentes eventos após o pulo. Default: null</param>
        /// <param name="onFinishAction">Novo callback a ser executado quando a animação terminar. Se null, nenhum callback será agendado.</param>
        /// <returns>True se o salto foi realizado com sucesso</returns>
        public bool JumpToEvent(string eventName, int trackIndex = 0, List<(string eventName, Action action)> eventActions = null, Action onFinishAction = null)
        {
            if (!ValidateSkeletonAnimation())
                return false;

            TrackEntry currentTrackEntry = _skeletonAnimation.AnimationState.GetCurrent(trackIndex);

            if (currentTrackEntry == null)
            {
                Debug.LogWarning($"[SpineHandler] TrackEntry não encontrada no índice {trackIndex} em '{gameObject.name}'", this);
                return false;
            }

            if (_animationsDataDict.TryGetValue(currentTrackEntry.Animation.Name, out SpineAnimationData animData))
            {
                if (animData.TryGetEventTime(eventName, out float eventTime))
                {
                    currentTrackEntry.TrackTime = eventTime;

                    // Cancela o callback pendente e agenda o novo (se fornecido)
                    CancelPendingFinishCallback(trackIndex);
                    ScheduleEventActions(currentTrackEntry.Animation.Name, eventTime, eventActions);
                    ScheduleFinishCallback(trackIndex, currentTrackEntry.Animation.Duration, eventTime, 0, onFinishAction);

                    return true;
                }
            }

            Debug.LogWarning($"[SpineHandler] Evento '{eventName}' não encontrado na animação '{currentTrackEntry.Animation.Name}' em '{gameObject.name}'", this);
            return false;
        }
        #endregion

        #region CALLBACK MANAGEMENT
        /// <summary>
        /// Agenda callbacks para eventos específicos após um JumpTo.
        /// </summary>
        private void ScheduleEventActions(string animationName, float currentTime, List<(string eventName, Action action)> eventActions)
        {
            if (eventActions == null || eventActions.Count == 0)
                return;

            foreach (var (eventName, action) in eventActions)
            {
                if (action == null || string.IsNullOrEmpty(eventName))
                    continue;

                if (TryGetEventTime(animationName, eventName, out float eventTime))
                {
                    float deltaTime = eventTime - currentTime;
                    if (deltaTime > 0)
                    {
                        _delayCalls.DelayedCall(action, deltaTime);
                    }
                    else
                    {
                        Debug.LogWarning($"[SpineHandler] Evento '{eventName}' ({eventTime:F2}s) é menor ou igual ao tempo atual ({currentTime:F2}s). Callback não será executado em '{gameObject.name}'", this);
                    }
                }
                else
                {
                    Debug.LogError($"[SpineHandler] Evento '{eventName}' não encontrado na animação '{animationName}' em '{gameObject.name}'", this);
                }
            }
        }

        /// <summary>
        /// Agenda um callback para quando a animação terminar.
        /// Armazena a referência do callback para permitir cancelamento posterior.
        /// </summary>
        private void ScheduleFinishCallback(int trackIndex, float animDuration, float currentTime, int playTimes, Action onFinishAction)
        {
            if (onFinishAction == null)
                return;

            // playTimes < 0 = loop infinito, não agenda callback de fim
            if (playTimes < 0)
                return;

            float timeUntilFinish = animDuration - currentTime;

            // Se playTimes > 1, adiciona o tempo das repetições adicionais
            if (playTimes > 1)
            {
                timeUntilFinish += animDuration * (playTimes - 1);
            }

            if (timeUntilFinish > 0)
            {
                _delayCalls.DelayedCall(onFinishAction, timeUntilFinish);
                _pendingFinishCallbacks[trackIndex] = onFinishAction;
            }
        }

        /// <summary>
        /// Cancela o callback de onFinishAction pendente para uma track específica.
        /// </summary>
        private void CancelPendingFinishCallback(int trackIndex)
        {
            if (_pendingFinishCallbacks.TryGetValue(trackIndex, out Action pendingCallback) && pendingCallback != null)
            {
                _delayCalls.RemoveCall(pendingCallback);
                _pendingFinishCallbacks[trackIndex] = null;
            }
        }
        #endregion

        /// <summary>
        /// Retorna o tempo de um evento específico em uma animação.
        /// </summary>
        /// <param name="animationName">Nome da animação</param>
        /// <param name="eventName">Nome do evento</param>
        /// <param name="time">Tempo do evento em segundos (out)</param>
        /// <returns>True se o evento foi encontrado</returns>
        public bool TryGetEventTime(string animationName, string eventName, out float time)
        {
            if (_animationsDataDict.TryGetValue(animationName, out SpineAnimationData animData))
                return animData.TryGetEventTime(eventName, out time);

            time = 0;
            return false;
        }

        /// <summary>
        /// Retorna todos os eventos de uma animação específica.
        /// </summary>
        /// <param name="animationName">Nome da animação</param>
        /// <returns>Lista somente leitura de eventos ou null se a animação não for encontrada</returns>
        public IReadOnlyList<SpineAnimationEventData> GetAllEvents(string animationName)
        {
            if (_animationsDataDict.TryGetValue(animationName, out SpineAnimationData animData))
                return animData.GetEvents();

            return null;
        }

        /// <summary>
        /// Retorna informações sobre uma animação específica.
        /// </summary>
        /// <param name="animationName">Nome da animação</param>
        /// <returns>SpineAnimationData ou null se não encontrada</returns>
        public SpineAnimationData GetAnimationData(string animationName)
        {
            _animationsDataDict.TryGetValue(animationName, out SpineAnimationData animData);
            return animData;
        }

        /// <summary>
        /// Para uma animação em uma track, fazendo blend para o setup pose.
        /// </summary>
        /// <param name="trackIndex">Índice da track</param>
        /// <param name="mixDuration">Duração do blend para o setup pose (0 = imediato)</param>
        public void StopAnimation(int trackIndex, float mixDuration = 0f)
        {
            if (!ValidateSkeletonAnimation())
                return;

            // SetEmptyAnimation faz blend para o setup pose
            _skeletonAnimation.AnimationState.SetEmptyAnimation(trackIndex, mixDuration);
        }

        /// <summary>
        /// Limpa uma track imediatamente, parando a animação sem blend.
        /// ATENÇÃO: A última pose aplicada permanece visível. Use StopAnimation() 
        /// se quiser voltar ao setup pose com blend suave.
        /// 
        /// Quando usar ClearTrack:
        /// - Reset emergencial (ex: skip de cutscene)
        /// - Antes de destruir/desativar o GameObject
        /// - Quando vai sobrescrever com outra animação imediatamente
        /// - Limpeza de tracks não utilizadas por performance
        /// </summary>
        /// <param name="trackIndex">Índice da track a ser limpa</param>
        public void ClearTrack(int trackIndex)
        {
            if (!ValidateSkeletonAnimation())
                return;

            _skeletonAnimation.AnimationState.ClearTrack(trackIndex);
        }
    }


    /// <summary>
    /// Contém dados de uma animação Spine, incluindo todos os seus eventos.
    /// </summary>
    [Serializable]
    public class SpineAnimationData
    {
        private readonly Spine42_Animation _animation;
        private readonly Dictionary<string, float> _eventTimesDict;
        private readonly List<SpineAnimationEventData> _events;

        public string Name => _animation.Name;
        public float Duration => _animation.Duration;

        public SpineAnimationData(Spine42_Animation animation)
        {
            _animation = animation;
            _events = new List<SpineAnimationEventData>();
            _eventTimesDict = new Dictionary<string, float>();
        }

        /// <summary>
        /// Adiciona um evento à lista de eventos desta animação.
        /// </summary>
        internal void AddEvent(string eventName, float eventTime)
        {
            var eventData = new SpineAnimationEventData(eventName, eventTime);
            _events.Add(eventData);
            _eventTimesDict[eventName] = eventTime;
        }

        /// <summary>
        /// Tenta obter o tempo de um evento específico.
        /// </summary>
        public bool TryGetEventTime(string eventName, out float time)
        {
            return _eventTimesDict.TryGetValue(eventName, out time);
        }

        /// <summary>
        /// Retorna uma lista somente leitura de todos os eventos.
        /// </summary>
        public IReadOnlyList<SpineAnimationEventData> GetEvents()
        {
            return _events.AsReadOnly();
        }
    }

    /// <summary>
    /// Representa um evento da timeline de uma animação Spine.
    /// </summary>
    [Serializable]
    public class SpineAnimationEventData
    {
        public string Name { get; }
        public float Time { get; }

        public SpineAnimationEventData(string name, float time)
        {
            Name = name;
            Time = time;
        }

        public override string ToString()
        {
            return $"Event: {Name} @ {Time:F2}s";
        }
    }
}

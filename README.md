# SpineHandler - Documentação

## Visão Geral
`SpineHandler` é um helper para manipulação de animações Spine. Ele oferece uma interface para reproduzir animações, pular para eventos/tempos/porcentagens específicas, e consultar informações sobre as animações, como eventos.

## Setup
1. Adicione o componente `SpineHandler` a um GameObject
2. Arraste a referência de um `Spine42_SkeletonAnimation` no Inspector
3. O componente irá automaticamente mapear todos os eventos das animações no `Start()`

## Principais Funcionalidades

### 1. Reproduzir Animações

#### Exemplos Básicos
```csharp
// Reproduzir animação simples (toca 1x - default)
spineHandler.PlayAnimation("idle");

// Com loop infinito (playTimes < 0)
spineHandler.PlayAnimation("walk", playTimes: -1);

// Tocar 3 vezes
spineHandler.PlayAnimation("attack", playTimes: 3);
```

#### Controle de Repetição (playTimes)
```csharp
// playTimes = 1  -> Toca 1x - DEFAULT
// playTimes = 0  -> Não toca (early return)
// playTimes < 0  -> Loop infinito
// playTimes > 1  -> Toca X vezes

// Loop infinito para idle
spineHandler.PlayAnimation("idle", playTimes: -1);

// Animação de mastigar 5 vezes
spineHandler.PlayAnimation("chew", playTimes: 5);
```

#### Controle de Início (startAt)
```csharp
// Começar em tempo absoluto específico
spineHandler.PlayAnimation("attack", startAtTime: 0.5f);

// Começar em porcentagem da duração (50%)
spineHandler.PlayAnimation("attack", startAtPercentage: 0.5f);

// Começar em evento específico
spineHandler.PlayAnimation("attack", startAtEvent: "wind_up");
```

#### Callbacks em Tempo Específico (onReach)
```csharp
// Callback em tempo ABSOLUTO na animação
spineHandler.PlayAnimation(
    animationName: "jump",
    onReachTime: 1.2f,  // Dispara em 1.2s da animação (independente de startAt)
    onReachAction: () => Debug.Log("Ápice do pulo!")
);

// Callback em porcentagem da duração total
spineHandler.PlayAnimation(
    animationName: "dash",
    onReachPercentage: 0.5f,  // Dispara no meio da animação
    onReachAction: () => Debug.Log("Meio do dash!")
);

// Callback em evento específico
spineHandler.PlayAnimation(
    animationName: "attack",
    onReachEvent: "impact",
    onReachAction: () => ApplyDamage()
);
```

#### Múltiplas Ações (Listas)
Para agendar várias ações em diferentes momentos da animação, use as listas:

```csharp
// Múltiplas ações por TEMPO ABSOLUTO
spineHandler.PlayAnimation(
    animationName: "combo",
    timeActions: new List<(float, Action)>
    {
        (0.5f, () => PlaySound("whoosh")),
        (1.2f, () => SpawnParticles()),
        (2.0f, () => CameraShake())
    }
);

// Múltiplas ações por PORCENTAGEM
spineHandler.PlayAnimation(
    animationName: "charge",
    percentageActions: new List<(float, Action)>
    {
        (0.25f, () => ShowProgress(25)),
        (0.50f, () => ShowProgress(50)),
        (0.75f, () => ShowProgress(75))
    }
);

// Múltiplas ações por EVENTO
spineHandler.PlayAnimation(
    animationName: "attack",
    eventActions: new List<(string, Action)>
    {
        ("wind_up", () => PlaySound("whoosh")),
        ("hit", () => ApplyDamage()),
        ("recovery", () => ResetState())
    }
);

// Combinando todas as listas
spineHandler.PlayAnimation(
    animationName: "special_attack",
    timeActions: new List<(float, Action)> { (0.1f, () => FlashScreen()) },
    percentageActions: new List<(float, Action)> { (0.5f, () => Midpoint()) },
    eventActions: new List<(string, Action)> { ("impact", () => Explode()) },
    onFinishAction: () => OnComplete()
);
```

#### Callback ao Terminar
```csharp
// Callback ao terminar (considera startAt)
spineHandler.PlayAnimation(
    animationName: "attack",
    onFinishAction: () => Debug.Log("Ataque terminou!")
);

// Combinando start custom com finish
spineHandler.PlayAnimation(
    animationName: "attack",
    startAtTime: 0.3f,  // Começa em 0.3s
    onFinishAction: () => Debug.Log("Terminou!")  // Dispara após (duration - 0.3s)
);
```

#### Exemplo Avançado: Animação de Bomba com Skip
```csharp
// Cenário: Animação de mastigar bomba com possibilidade de skip
// Eventos na timeline: CHEW_LVL1, CHEW_LVL2, CHEW_LVL3, SPARKS_ON, EXPLOSION

private void PlayChewBombAnimation()
{
    _spineHandler.PlayAnimation(
        animationName: "chew_bomb",
        eventActions: new List<(string, Action)>
        {
            ("CHEW_LVL1", () => OnChewLevel(1)),
            ("CHEW_LVL2", () => OnChewLevel(2)),
            ("CHEW_LVL3", () => OnChewLevel(3))
        },
        onFinishAction: () => PlayExplosionAnimation()
    );
}

// Skip para explosão quando jogador toca na tela
private void OnPlayerTap()
{
    // JumpToEvent cancela automaticamente o onFinishAction anterior
    // e agenda um novo callback
    _spineHandler.JumpToEvent(
        eventName: "EXPLOSION",
        onFinishAction: () => PlayExplosionAnimation()
    );
}

// Exemplo com playTimes: mastigar N vezes antes de explodir
private void PlayChewLoop(int chewCount)
{
    _spineHandler.PlayAnimation(
        animationName: "chew_loop",
        playTimes: chewCount,  // Toca exatamente chewCount vezes
        onFinishAction: () => PlayExplosionAnimation()
    );
}
```

### 2. Pular para Eventos/Tempos/Porcentagens

Os métodos `JumpTo` permitem pular para pontos específicos da animação em execução. Eles **cancelam automaticamente** qualquer `onFinishAction` pendente e permitem reagendar novos callbacks.

#### Básico
```csharp
// Pular para evento (animação deve estar rodando)
spineHandler.JumpToEvent("explosion_start");

// Pular para tempo absoluto
spineHandler.JumpToTime(2.5f);

// Pular para porcentagem
spineHandler.JumpToPercentage(0.75f);  // 75% da duração

// Especificar track de animação
spineHandler.JumpToEvent("loop_point", trackIndex: 1);
```

#### Com Callbacks após o Pulo
```csharp
// Pular para evento com callback ao terminar a animação
spineHandler.JumpToEvent(
    eventName: "CHEW_LVL3",
    onFinishAction: () => PlayExplosionAnimation()
);

// Pular para evento com múltiplas ações baseadas em eventos
spineHandler.JumpToEvent(
    eventName: "CHEW_LVL3",
    eventActions: new List<(string, Action)>
    {
        ("SPARKS_ON", () => SparkOff()),
        ("SOUND_CUE", () => PlaySound())
    },
    onFinishAction: () => PlayExplosionAnimation()
);

// Também funciona com JumpToTime e JumpToPercentage
spineHandler.JumpToTime(
    time: 2.5f,
    eventActions: new List<(string, Action)> { ("hit", () => ApplyDamage()) },
    onFinishAction: () => OnComplete()
);
```

#### Comportamento de Cancelamento Automático
Quando você chama qualquer método `JumpTo`, o `onFinishAction` pendente da chamada anterior é **automaticamente cancelado**. Isso evita que callbacks sejam disparados com timing incorreto após um pulo na timeline.

```csharp
// Exemplo: Animação de mastigar com pulo
_spineHandler.PlayAnimation(
    animationName: "chew_bomb",
    eventActions: new List<(string, Action)>
    {
        ("CHEW_LVL1", () => _spineHandler.JumpToEvent(
            eventName: "CHEW_LVL3",
            eventActions: new List<(string, Action)> { ("SPARKS_ON", () => SparkOff()) },
            onFinishAction: () => PlayExplosionAnimation() // Este substitui o original
        ))
    },
    onFinishAction: () => PlayAnimationIdle() // Este será cancelado quando JumpToEvent for chamado
);
```

### 3. Parar Animações

#### StopAnimation - Com blend suave
```csharp
// Para a animação fazendo blend para o setup pose
spineHandler.StopAnimation(trackIndex: 0);

// Com duração de blend customizada (0.3 segundos)
spineHandler.StopAnimation(trackIndex: 0, mixDuration: 0.3f);

// Parar imediatamente (sem blend)
spineHandler.StopAnimation(trackIndex: 0, mixDuration: 0f);
```

#### ClearTrack - Reset imediato
```csharp
// Limpa a track imediatamente SEM blend
// ATENÇÃO: A última pose aplicada permanece visível!
spineHandler.ClearTrack(trackIndex: 0);

// Quando usar ClearTrack:
// - Skip de cutscene (reset emergencial)
// - Antes de destruir/desativar o GameObject
// - Quando vai sobrescrever com outra animação imediatamente
// - Limpeza de tracks não utilizadas por performance
```

### 4. Consultar Informações
```csharp
// Obter tempo de evento
if (spineHandler.TryGetEventTime("attack", "hit_frame", out float hitTime))
{
    Debug.Log($"Hit ocorre em {hitTime}s");
}

// Obter todos os eventos
var events = spineHandler.GetAllEvents("combo_animation");
foreach (var evt in events)
{
    Debug.Log($"{evt.Name} @ {evt.Time}s");
}

// Obter dados completos da animação
var animData = spineHandler.GetAnimationData("special_move");
if (animData != null)
{
    Debug.Log($"Duração: {animData.Duration}s");
    Debug.Log($"Total de eventos: {animData.GetEvents().Count}");
}
```

## Assinatura Completa do PlayAnimation

```csharp
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
    int playTimes = 1  // 1 = toca 1x (default), 0 = não toca, < 0 = infinito, > 1 = toca X vezes
)
```

## Estrutura de Dados

### SpineAnimationData
Contém informações sobre uma animação:
- `Name`: Nome da animação
- `Duration`: Duração total em segundos
- `TryGetEventTime(eventName, out time)`: Busca tempo de evento
- `GetEvents()`: Retorna lista readonly de todos os eventos

### SpineAnimationEventData
Representa um evento individual:
- `Name`: Nome do evento
- `Time`: Tempo do evento em segundos
- `ToString()`: Retorna formato legível "Event: {Name} @ {Time}s"

## Precedência de Parâmetros

Quando múltiplos parâmetros são fornecidos, a precedência é:

### StartAt (onde começar)
1. **startAtTime** (prioridade máxima)
2. **startAtPercentage**
3. **startAtEvent** (prioridade mínima)

### OnReach (quando disparar callback)
1. **onReachTime** (prioridade máxima)
2. **onReachPercentage**
3. **onReachEvent** (prioridade mínima)

⚠️ Um **warning** será logado se você fornecer múltiplos parâmetros conflitantes.

## Conceitos Importantes

### ⏱️ Tempos Absolutos vs Relativos

| Parâmetro           | Tipo                                        | Exemplo                                      |
| ------------------- | ------------------------------------------- | -------------------------------------------- |
| `startAtTime`       | **Absoluto à timeline da animação (spine)** | 0.5f = começa em 0.5s da animação            |
| `onReachTime`       | **Absoluto à timeline da animação (spine)** | 1.2f = callback em 1.2s da animação          |
| `onReachPercentage` | **Relativo à timeline da animação (spine)** | 0.5f = callback em 50% da animação           |
| `onFinishAction`    | **Relativo ao startAt + playTimes**         | Dispara após (duration - startTime) × plays  |
| `playTimes`         | **Quantidade de execuções**                 | 1 = 1x (default), 0 = não toca, -1 = infinito |

### 📊 Comportamento de Callbacks

```csharp
// Animação de 4 segundos
// Exemplo 1: startAt + onReachTime
spineHandler.PlayAnimation(
    animationName: "attack",
    startAtTime: 1.0f,           // Começa em 1s
    onReachTime: 2.5f,           // Callback em 2.5s (absoluto na timeline)
    onReachAction: DoSomething,  // Dispara após 1.5s de playback (2.5 - 1.0)
    onFinishAction: OnFinish     // Dispara após 3s de playback (4.0 - 1.0)
);

// Exemplo 2: onReachPercentage
spineHandler.PlayAnimation(
    animationName: "attack",
    startAtTime: 1.0f,           // Começa em 1s
    onReachPercentage: 0.5f,     // 50% da duração total = 2s
    onReachAction: DoSomething   // Dispara após 1s de playback (2.0 - 1.0)
);

// Exemplo 3: Com playTimes (animação de 2s, toca 4 vezes)
spineHandler.PlayAnimation(
    animationName: "chew",
    playTimes: 4,                // Toca 4 vezes
    onFinishAction: OnFinish     // Dispara após 8s (2s × 4)
);

// Exemplo 4: startAt + playTimes
spineHandler.PlayAnimation(
    animationName: "chew",
    startAtTime: 0.5f,           // Começa em 0.5s
    playTimes: 3,                // Toca 3 vezes
    onFinishAction: OnFinish     // Dispara após: (2.0 - 0.5) + (2.0 × 2) = 5.5s
);
```

### ⚠️ Validações Automáticas

O `SpineHandler` valida automaticamente e loga warnings quando:
- Callback ocorrer antes ou no mesmo tempo que o início
- Múltiplos parâmetros conflitantes são fornecidos
- Evento não existe na animação
- Tempo está fora dos limites da animação

## Boas Práticas

### ✅ Faça
- Use `TryGetEventTime` para obter tempos dinamicamente
- Valide retornos dos métodos `JumpTo*`
- Use **tempos absolutos** para `onReachTime` (não some ao startAt)
- Use o sistema de tracks para animações simultâneas
- Use eventos do Spine sempre que possível (mais robusto que hardcoded)
- Use `playTimes: -1` para loops infinitos (idle, backgrounds)
- Use `StopAnimation()` com `mixDuration` para transições suaves

### ❌ Evite
- Modificar listas retornadas por `GetAllEvents()` (são readonly)
- Chamar `PlayAnimation` antes do `Start()` completar
- Misturar múltiplos parâmetros startAt/onReach (use apenas um de cada tipo)
- Usar `playTimes: -1` esperando que callbacks disparem a cada loop (só disparam 1x)
- Assumir que eventos existem sem validar
- Usar `ClearTrack()` quando quer transição suave (use `StopAnimation()` ao invés)

## Exemplo: Sistema de Vitória (Pull Tab)

```csharp
public class WinCelebrationController : MonoBehaviour 
{
    [SerializeField] private SpineHandler _spineHandler;
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private ParticleSystem _particles;

    /// <summary>
    /// Executa celebração de vitória com múltiplos efeitos sincronizados
    /// </summary>
    public void PlayWinCelebration(int winLevel)
    {
        string animName = $"win_celebration_lvl{winLevel}";
        
        _spineHandler.PlayAnimation(
            animationName: animName,
            eventActions: new List<(string, Action)>
            {
                ("COINS_START", () => _particles.Play()),
                ("SOUND_FANFARE", () => _audioSource.Play()),
                ("COINS_END", () => _particles.Stop())
            },
            onFinishAction: () => OnCelebrationComplete()
        );
    }

    /// <summary>
    /// Animação idle em loop infinito
    /// </summary>
    public void PlayIdle()
    {
        _spineHandler.PlayAnimation(
            animationName: "idle",
            playTimes: -1  // Loop infinito
        );
    }

    /// <summary>
    /// Skip da celebração quando jogador toca
    /// </summary>
    public void SkipCelebration()
    {
        _spineHandler.JumpToEvent(
            eventName: "CELEBRATION_END",
            onFinishAction: () => OnCelebrationComplete()
        );
    }

    private void OnCelebrationComplete()
    {
        PlayIdle();
    }
}

```

## Performance

### Otimizações Implementadas
- **Dictionary lookup O(1)**: Eventos e animações usam `Dictionary` para acesso instantâneo
- **Cacheamento**: Eventos mapeados uma vez no `Start()`
- **Readonly collections**: Previne alocações desnecessárias
- **Validação early-return**: Checa condições antes de processamento pesado
- **If-else em cascata**: Evita múltiplas chamadas desnecessárias

## Debugging
```csharp
// Ver todos os eventos de uma animação
var events = spineHandler.GetAllEvents("debug_animation");
if (events != null)
{
    Debug.Log($"Total eventos: {events.Count}");
    foreach (var evt in events)
    {
        Debug.Log(evt);  // Usa ToString() customizado
    }
}

// Ver duração
var animData = spineHandler.GetAnimationData("test");
Debug.Log($"Duração: {animData?.Duration ?? 0}s");

// Testar se evento existe
if (spineHandler.TryGetEventTime("attack", "impact", out float time))
{
    Debug.Log($"Evento 'impact' existe em {time}s");
}
else
{
    Debug.LogWarning("Evento 'impact' não encontrado!");
}
```

## Troubleshooting

| Problema                          | Causa Provável                               | Solução                                          |
| --------------------------------- | -------------------------------------------- | ------------------------------------------------ |
| Eventos não encontrados           | Animação não mapeada ou nome incorreto       | Verifique nome exato no Spine Editor             |
| Callback não dispara              | `onReachTime` < `startAtTime`                | Use tempo absoluto maior que o início            |
| Callback dispara imediatamente    | `onReachTime` = `startAtTime`                | Ajuste o tempo ou use `onReachPercentage`        |
| Warning de parâmetros múltiplos   | Forneceu `startAtTime` E `startAtPercentage` | Use apenas um parâmetro de cada tipo             |
| `JumpToEvent` retorna false       | Animação não está rodando na track           | Chame `PlayAnimation` antes de `JumpToEvent`     |
| NullReferenceException            | `SkeletonAnimation` não atribuído            | Arraste referência no Inspector                  |
| Callback em loop não funciona     | Usando `playTimes: -1` (infinito)            | Callbacks só disparam 1x, não em cada iteração   |
| `onFinishAction` não dispara      | `playTimes < 0` (infinito)                   | Loop infinito nunca termina, use `eventActions`  |
| Animação não toca                 | `playTimes: 0`                               | Use `playTimes: 1` ou omita (default é 1)        |
| Animação não para suavemente      | Usando `ClearTrack()` ao invés de `Stop`     | Use `StopAnimation(trackIndex, mixDuration)`     |
| Pose estranha após parar          | `ClearTrack()` mantém última pose            | Use `StopAnimation()` para voltar ao setup pose  |

## Limitações Conhecidas

1. **Callbacks não repetem em loop infinito**: Se `playTimes < 0`, callbacks só disparam na primeira execução. `onFinishAction` nunca será chamado pois a animação não termina.
2. **eventActions de JumpTo não são canceláveis**: Ao contrário do `onFinishAction`, os `eventActions` agendados após um JumpTo não podem ser cancelados individualmente.
3. **ClearTrack mantém última pose**: Ao usar `ClearTrack()`, o skeleton permanece na última pose aplicada. Use `StopAnimation()` se quiser voltar ao setup pose.
4. **playTimes com startAt**: O tempo inicial só afeta a primeira iteração. Iterações subsequentes começam do início da animação.

## Changelog
- **v1.6**: Renomeado `loopCount` para `playTimes` com semântica mais intuitiva: `1` = toca 1x (default), `0` = não toca, `-1` = infinito, `N` = toca N vezes
- **v1.5**: Substituído parâmetro `loop` por `loopCount` (int); adicionados métodos `StopAnimation()` e `ClearTrack()`
- **v1.4**: Métodos `JumpTo` agora suportam `eventActions` e `onFinishAction`; cancelamento automático de callbacks pendentes
- **v1.3**: Adicionadas listas de ações múltiplas: `timeActions`, `percentageActions`, `eventActions`
- **v1.2**: Refatoração completa com startAt/onReach por evento/tempo/porcentagem
- **v1.1**: Adicionados `onFinishAction` e `onReachPercentage`
- **v1.0**: Implementação inicial

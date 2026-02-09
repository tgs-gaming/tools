# Asset Dependency Manager (TGS)

Este documento descreve a ferramenta "Asset Dependency Manager" para uso interno. Ela permite duplicar assets e pastas com dependencias, substituir referencias e exportar/importar pacotes TGS mantendo GUIDs e referencias internas.

## Menus

Menus de contexto (Project Window, clique com o botao direito em assets/pastas):
- Assets/TGS/Asset Dependencies/Manager
- Assets/TGS/Asset Dependencies/Import Package
- Assets/TGS/Asset Dependencies/Export Package

Menu superior do Unity:
- TGS/Asset Dependencies/Manager
- TGS/Asset Dependencies/Import Package
- TGS/Asset Dependencies/Export Package

## Janela "Manager"

### Selected Assets
- Lista de selecao (sempre com pelo menos uma entrada).
- Botao "+" adiciona linha vazia.
- Botao "-" remove a ultima linha (mantem 1 entrada vazia).
- Botao "Browse" abre o Object Picker para trocar o asset.
- Botao "Select" faz ping no asset no Project.

Pastas sao suportadas:
- Ao selecionar uma pasta, a ferramenta considera todos os assets e subpastas dela.
- A lista mostra apenas a pasta, mas as operacoes usam o conteudo completo.

### Dependencies
- Game-Related Dependencies (N)
- System & Common & Script Dependencies (N)

As dependencias sao calculadas com base nos assets selecionados, incluindo o conteudo de pastas.

### Abas

#### Copy To
- Destination: pasta destino (botao Browse abre seletor de pastas).
- SubFolder: define a subpasta onde o conteudo sera copiado.
    - Se estiver vazio, nenhum subfolder eh criado (copia direto na pasta destino).
- Code References (foldout):
    - Mostra somente se existirem namespaces ou arquivos .asmdef nos assets selecionados ou dependencias.
    - Agrupa por padrao e permite renomear:
        - Namespaces
        - Assembly Definition filenames (.asmdef)
        - Root Namespaces (asmdef)
        - Asmdef Names (campo "name" do asmdef)
    - Os campos da direita sao preenchidos por padrao com o valor atual.
    - O botao "Duplicate Asset & Dependencies" so fica ativo se todos os campos estiverem preenchidos.
- Preview: lista a estrutura final que sera gerada no destino (inclui renomes de .asmdef).
- Duplicate Asset & Dependencies:
    - Copia assets/pastas e dependencias externas.
    - Mantem estrutura original para pastas.
    - Dependencias externas sao copiadas para "_Dependencies" (quando nao fazem parte da selecao).
    - GUIDs sao recriados e referencias internas sao atualizadas.
    - Code References (namespace/asmdef) sao aplicadas durante a copia.

#### Replace References
- Mostra referencias diretas detectadas e permite:
    - Substituir referencias por outro asset.
    - Remover referencias (gera GUID vazio).
- Botao "Replace References" executa a troca nas selecoes.

#### Export & Import
- Code References (mesma logica da aba Copy To).
- Export TGS Package:
    - Exporta os assets selecionados com dependencias.
    - Aplica renomes de Code References e remapeia GUIDs antes do zip.
- Import TGS Package:
    - Seleciona o arquivo .tgspackage e a pasta destino.
    - Aplica renomes de Code References e remapeia GUIDs ao importar.

## Regras importantes

- Sempre ha ao menos um campo em Selected Assets.
- SubFolder vazio significa copia direta no destino.
- Renomes de Code References sao obrigatorios para duplicar/exportar/importar (campos nao podem ficar vazios).
- Para pastas selecionadas, dependencias internas nao sao jogadas em "_Dependencies".
- A ferramenta recalcula caches quando o projeto muda (import/refresh).

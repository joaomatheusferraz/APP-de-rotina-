# Validação da fundação, hábitos, check-in, planejamento, metas, corpo, treinos, foco e sonhos

## Testes automatizados

`dotnet test tests/Rotina.Tests --no-restore`: 62 testes aprovados, nenhum com falha.

- Criação do banco vazio, com tema padrão e sem dados pessoais inventados.
- Persistência de perfil após reabrir o banco e inicializações simultâneas.
- Edição e arquivamento de área preservando identidade.
- Nomes tratados como dados SQL, sem interpolação; ordenação determinística.
- Recusa de esquema futuro sem sobrescrever o perfil existente.
- Rejeição de nome vazio e cores inválidas.
- Data local em Manaus distinta da data UTC, inclusive à meia-noite local.

Os testes adicionais cobrem descanso, dia aberto, pausa, meta semanal, revisão no meio da semana, correção e nota como eventos separados, migração do banco v1 para v2, edição sem perder conclusão antiga, arquivamento/restauração sem perder o intervalo inativo e rejeição de conclusão futura. Para o check-in, cobrem virada do dia, água e correções auditáveis, manutenção da meta e da escala registradas, revisão de check-in, fechamento/reabertura, migração v2 para v3 sem perder hábitos e rejeição de registros futuros. São testes de SQLite real em diretório temporário.

Os quatro testes de planejamento verificam tarefa pontual com conclusão, reabertura, edição e arquivamento; compromisso com instante, lembrete e arquivamento persistidos; migração v3 para v4 preservando perfil e água; e rejeição de duração ou lembrete inválidos.

Os sete testes de metas verificam banco novo sem Projeto 80 kg ou pesagens criadas automaticamente, progresso numérico e histórico após concluir/reabrir, pesagens independentes da meta, progresso de ganho e perda de peso, vínculos com hábitos e tarefas existentes, mudanças de coluna auditáveis no Kanban, migração v4 para v5 preservando tarefas e rejeição de peso futuro.

Os testes da etapa 6 verificam medidas independentes e cronológicas, correções de peso e medida sem apagar o valor anterior, desconsideração que preserva o histórico mas exclui os gráficos, migração v5 para v6 mantendo metas e pesagens, rejeição de medidas futuras e comparação de hábitos entre períodos com descanso separado de faltas.

Os cinco testes de treino verificam troca de versão preservando plano e sessão antigos, série vinculada apenas ao exercício da sessão aberta, bloqueio de série duplicada, volume, ciclo com pausa/retomada/substituição, frequência com descanso e falta separados e migração v6 para v7 preservando medidas.

Os três testes da etapa 8 verificam pausa, retomada e duração real de foco, avaliação e nota, bloqueio de sessões simultâneas, rejeição de horários futuros e migração v7 para v8.

Os três testes da etapa 9 verificam vínculos opcionais com metas, reordenação sem perder a posição dos outros cartões, arquivamento, validação de imagem e migração v8 para v9.

## Android

Compilação final e assinatura Debug concluídas com zero avisos e zero erros, incluindo as assemblies no APK para instalação direta sem fast deployment. O pacote tem identificador `br.pessoal.rotina`, versão `1.0.0` (código 10), Android mínimo API 23 e alvo API 35. Inspeção de pacote anterior encontrou as bibliotecas nativas SQLite para arm64-v8a e x86_64.

APK de desenvolvimento disponível em `artifacts/Rotina-1.0.0-debug.apk`. A pasta de artefatos não deve ser versionada. A assinatura é de desenvolvimento, não uma chave de distribuição.

O Windows reconheceu o moto g84 5G por USB e o ADB o lista como dispositivo autorizado. O APK 1.0.0 foi instalado sobre a versão anterior com `adb install -r` (`Success`), preservando os dados, e abriu como atividade principal no Android 15 / API 35. A versão instalada foi confirmada pelo gerenciador de pacotes do aparelho. A aba Rotina, o atalho Quadro dos Sonhos, a tela vazia do mural e o seletor de imagens da galeria foram inspecionados por hierarquia de UI. O seletor foi cancelado sem copiar imagens para o app. As telas das etapas anteriores foram verificadas nas entregas correspondentes. O manifesto Debug inclui INTERNET, adicionada pela ferramenta de depuração, embora o código do app não faça chamadas de rede.

O fluxo completo de salvar um plano, executar séries, iniciar uma sessão real de foco, escolher uma imagem e reabrir com dados reais ainda exige teste manual no aparelho com dados escolhidos pelo usuário; a entrega de uma notificação futura, o caminho externo para o calendário Android e a escala de fonte ampliada também não foram verificados. As regras de persistência, recorrência, metas, corpo, treinos, foco, sonhos e histórico foram verificadas nos testes automatizados. O roteiro manual está no README.

Os três testes da etapa 10 verificam layout padrão, persistência de ordem/visibilidade e migração v9 para v10.


A etapa 12 foi validada no moto g84 com instalação do APK 1.2.0 (código 12). A aba Perfil exibiu Backup e privacidade, Exportar dados, Criar backup local, Restaurar último backup local e Abrir diário após rolagem.


## Correção 1.2.3 — encerramento em Progresso/Perfil
Os logs da versão anterior confirmaram IllegalStateException: "The specified child already has a parent" ao trocar abas e fechar telas internas. Os filtros de GoalProgressPage, BodyPage e WorkoutProgressPage foram separados dos resultados que são reconstruídos. Perfil usa agora um template para a mensagem de áreas vazias.
Build Android: zero erros/avisos. Testes existentes: 62 aprovados.
APK 1.2.3, código 15, instalado por atualização no moto g84. Validados: Progresso 30→7 dias; Perfil → Padrões do check-in → Cancelar → Progresso; categoria Corpo → Peso e medidas → 90 dias → Fechar; categoria Treino → Evolução do treino → 7 dias → Fechar → Perfil. Processo 11056 permaneceu ativo, sem novo registro no buffer de crash durante esses fluxos. Nenhum registro pessoal foi criado ou restaurado.
Limite: gráficos com séries reais e restauração de backup não foram exercitados nesta correção.

## Cronômetro de descanso — 26/09/2026
- Painel fixo na sessão, opções de 60/90/120 segundos, pausa/retomada, +15 segundos e reinício.
- Salvar uma série inicia o descanso automaticamente. O estado é salvo nas preferências por sessão e removido ao finalizar o treino.
- A contagem usa um prazo UTC, incluindo o tempo fora da tela; o atualizador visual para quando a página sai de exibição. O término é indicado visualmente, sem alarme em segundo plano.
- Resumo da sessão com progresso de séries e volume acumulado.
- 65 testes aprovados, incluindo três testes de contagem, pausa/retomada e serialização do estado.
- Validação visual no Android pendente: o ADB não conseguiu iniciar no ambiente restrito.
- Roteiro manual: iniciar treino, salvar série, pausar/retomar, adicionar 15 s, aguardar zerar, fechar/reabrir durante a contagem, finalizar e conferir temas claro/escuro e fonte ampliada.
- APK atualizado gerado com sucesso: artifacts/Rotina-descanso-debug.apk. Compilação Android com zero avisos e zero erros.

## Visual 1.3.0 — 26/09/2026
- Nova paleta grafite/azul, ações principais violetas e indicadores ciano; temas claro/escuro preservados.
- Botões secundários com contorno, cantos arredondados e altura mínima de 52; estilo principal explícito para iniciar/retomar treino, concluir séries, criar hábitos e salvar preferências.
- Cartões com cantos mais suaves, navegação consistente e cabeçalhos em degradê nas telas Hoje, Rotina e Treino.
- Cronômetro de descanso incluído e harmonizado com a nova paleta.
- Testes automatizados: 65 aprovados.
- Build Android concluído com zero avisos/erros; APK em artifacts/Rotina-1.3.0-debug.apk.
- Instalado no moto g84 5G por atualização (adb install -r: Success), versão 1.3.0 / código 16 confirmados pelo Android.
- App aberto e telas Hoje/Treino conferidas por screenshot: cabeçalhos, botões, cartões e navegação renderizados sem cortes de texto visíveis. Dados existentes continuam aparecendo. Nenhuma sessão de treino foi criada para teste.
- Limites: tema claro, fonte ampliada e fluxo completo do cronômetro ainda precisam de conferência manual no aparelho.

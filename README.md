# Editrainer

Editor local de partidas de **todos los juegos principales de Pokémon** para Windows, desde Game Boy hasta Switch. Abre `Editrainer.exe` con doble clic. Necesita .NET Desktop Runtime 10. Al abrir una partida, la app adopta el nombre, los límites y las categorías de ese juego.

## Juegos compatibles

| Consola | Juegos | Archivo de guardado |
|---|---|---|
| Game Boy / Color | Rojo, Azul, Amarillo, Oro, Plata, Cristal | `.sav`, `.srm` |
| Game Boy Advance | Rubí, Zafiro, Esmeralda, Rojo Fuego, Verde Hoja | `.sav`, `.srm` |
| Nintendo DS | Diamante, Perla, Platino, HeartGold, SoulSilver, Negro, Blanco, Negro 2, Blanco 2 | `.sav`, `.dsv` |
| Nintendo 3DS | X, Y, Rubí Omega, Zafiro Alfa, Sol, Luna, Ultrasol, Ultraluna | `main` |
| Switch | Let's Go, Espada, Escudo, Diamante Brillante, Perla Reluciente, Leyendas: Arceus, Escarlata, Púrpura, Leyendas: Z-A | `main`, `savedata.bin` |
| GameCube | Colosseum, XD | `.gci` (sin probar: ver abajo) |

Abre siempre el **archivo de guardado** del juego, no un estado guardado del emulador (`.ss0`, `.ml1`…): esos no son partidas. **Buscar partidas** revisa las carpetas de Eden, Yuzu y derivados, Ryujinx, Citra, Azahar, Lime3DS, RetroArch, mGBA, melonDS, DeSmuME y Dolphin, además de la carpeta que contiene a la app. Si hay varias, te deja elegir.

### Qué cambia según la generación

- **Gen 1 y 2**: usan DV de 0 a 15 y EV de 0 a 65535, sin naturalezas. Los PS y el Especial se calculan a partir de los otros DV, así que esas casillas se bloquean donde corresponde. Gen 1 no tiene objetos equipados, amistad ni shiny. En Gen 2 el shiny depende de los DV: al marcarlo cambian.
- **Gen 3 y 4**: la naturaleza depende del PID del Pokémon. Al cambiarla, o al marcar shiny, la app busca un PID que conserve género, habilidad y forma. El juego lo acepta sin problema, pero un verificador de legalidad podría señalarlo.
- **Gen 3 a 5**: los EV llegan a 255 por estadística, con 510 en total. De Gen 6 en adelante, 252 por estadística.
- **Let's Go**: los EV se sustituyen por AV de 0 a 200, sin límite total. Los CP se recalculan.
- **Límites de dinero y bolsa**: cada juego usa los suyos, incluidos los espacios de cada bolsillo.
- Los objetos clave y los del PC no se editan. Las reglas de megapiedras y orbes solo se aplican en Z-A.

### Comprobación

- **Z-A** se ha probado con partidas reales.
- Los otros **37 juegos** se han probado de principio a fin con partidas generadas con la estructura real de cada formato (`tests --all-games`): abrir, editar, guardar con respaldo, reabrir y comparar. **No se han probado con partidas reales**: antes de confiar en un juego, haz una copia y compruébalo en el emulador.
- **Colosseum y XD** no se han podido probar, porque su contenedor cifrado de GameCube no se puede generar desde cero. La lectura y escritura las hace PKHeX, pero úsalos con una copia.
- En **Diamante, Perla y Platino** la lista de movimientos no incluye los tutores especiales, por un fallo de PKHeX 26.10.18. Los movimientos que ya conoce el Pokémon se conservan.

## Cómo usarlo

1. La app abre automáticamente tu partida más reciente. También puedes pulsar **Abrir partida**, **Buscar partidas** o arrastrar el archivo a la ventana.
2. Elige un Pokémon en el panel izquierdo, que imita la pantalla de cajas del juego: arriba el **Equipo** y abajo una caja con sus huecos vacíos. Cambia de caja con **◀ ▶** o con la lista, que indica cuántos Pokémon tiene cada una. La partida se abre en la última caja que usaste en el juego. Pasa el ratón por encima de un Pokémon para ver su nombre, nivel y lugar, o muévete con las flechas del teclado tras hacer clic en la cuadrícula. **✦** marca a los shiny y un punto verde a los que tienen cambios sin guardar. El cuadro de búsqueda encuentra por nombre, apodo o caja: atenúa el equipo que no coincide y muestra los resultados de todas las cajas en lugar de la caja abierta.
3. Modifica sus IV, EV, naturaleza, habilidad o movimientos. Las cifras de **Resultado** muestran sus estadísticas al nivel actual. La casilla **Shiny / variocolor**, junto al nombre y la imagen, permite activar o quitar el estado shiny; la ilustración y el sprite cambian al momento.
4. **Optimizar** propone IV de 31, naturaleza Firme o Modesta y 508 EV útiles. La casilla **Futuro** permite elegir una evolución o su forma actual. No cambia la especie ni el nivel. En evoluciones con varias ramas puedes elegir cuál prefieres.
5. **Optimizar todos** prepara a todos los Pokémon del equipo y las cajas, pensando en su evolución final. En ramas con varias opciones utiliza la evolución con mayor Ataque o Ataque Especial; revisa la selección individual si quieres otra rama.
6. En **Movimientos**, puedes escribir el nombre completo o un fragmento en cualquiera de las cuatro casillas. La lista se filtra sin distinguir mayúsculas ni tildes: `aereo` encuentra `Golpe Aéreo`. Selecciona el resultado con el ratón o pulsa **Enter** para confirmar. **Esc** cancela la búsqueda y conserva el movimiento equipado.
7. Cierra el emulador de esa consola y pulsa **Guardar partida**. La app verifica el archivo y conserva un respaldo anterior completo. **Guardar como** permite exportar una copia.

Los cambios permanecen en memoria hasta guardar. El botón **Deshacer este Pokémon** descarta las modificaciones pendientes de ese Pokémon. No cambia lo que ya se guardó en sesiones anteriores. `Ctrl+O` abre una partida y `Ctrl+S` guarda.

## Configuración: tema e idioma

El botón **⚙**, arriba a la izquierda, abre la configuración:

- **Tema**: claro u oscuro.
- **Idioma**: español, inglés, chino simplificado o francés. Cambia los textos de la app y los nombres de Pokémon, movimientos, habilidades, naturalezas, tipos y objetos.

Al pulsar **Aplicar**, el cambio se hace al momento, sin cerrar la ventana, y conserva la partida abierta, los cambios sin guardar, el Pokémon seleccionado, la caja y la pestaña. La elección se guarda en `settings.json`, junto a la app, y se recuerda al volver a abrirla.

- El idioma solo cambia lo que ves. Lo que se escribe en la partida es idéntico en cualquier idioma, y los apodos y nombres de entrenador siguen siendo los de la partida.
- Los botones de los avisos y las ventanas de abrir y guardar archivos los dibuja Windows, así que aparecen en el idioma del sistema.
- Solo se ofrecen idiomas con los nombres del juego completos. PKHeX no los tiene en portugués, ruso, hindi ni árabe: la app quedaría a medio traducir, por eso el tercer idioma es el francés.

## Formas y colores

Los Pokémon con varias formas muestran el selector **Forma** junto a **Shiny / variocolor**: diseños de Vivillon, colores de Flabébé, Minior y Squawkabilly, letras de Unown, cortes de Furfrou, formas regionales, Rotom, Oricorio, Deoxys y muchos más. Alcremie reúne su crema y su confite en una sola lista. Al elegir una forma se actualizan los tipos, las estadísticas, la habilidad y los movimientos disponibles. Se guarda como cualquier otro cambio y **Deshacer este Pokémon** la revierte.

- Solo aparecen las formas que cada juego conserva fuera de combate. No se ofrecen megaevoluciones, formas primigenias o de combate, fusiones (Kyurem, Necrozma, Calyrex), Pokémon dominantes ni formas que dependen del género (Meowstic, Indeedee, Basculegion, Oinkologne). Un Pokémon que ya tenga una de esas formas la conserva y no muestra el selector.
- Si la forma depende de un objeto (Arceus, Silvally, Genesect, Ogerpon, la forma Origen de Giratina, Dialga y Palkia), el objeto se equipa al elegirla si lo tienes en la bolsa. Si no lo tienes, la app avisa de cuál falta. Al volver a la forma normal, el objeto regresa a la bolsa.
- Gen 1 no tiene formas. En Gen 2 y 3 solo Unown guarda la suya: en Gen 2 la letra sale de los DV, así que elegirla cambia los DV, y un Unown shiny solo puede ser I o V. En Gen 3 sale del PID. Deoxys en Gen 3 depende del juego, no del Pokémon.
- Furfrou y Hoopa empiezan con su contador de días completo. Al terminar, el juego los devuelve a su forma normal.
- Si un movimiento solo lo podía aprender la forma anterior, se retira al cambiar de forma. Los que el Pokémon ya conocía en la partida se conservan.
- Algunas formas no se pueden conseguir de forma normal en ciertos juegos. El juego las acepta, pero un verificador de legalidad podría señalarlas.

## Habilidad

Elige un Pokémon y abre **Estadísticas**. El selector **Habilidad**, bajo la naturaleza, muestra la que tiene y permite cambiarla por la **principal**, la **secundaria** o la **oculta**, según las que tenga ese Pokémon en ese juego. Si dos espacios comparten habilidad, aparece una sola vez. El cambio se aplica con **Guardar partida** y **Deshacer este Pokémon** lo revierte.

- **Gen 1 y 2** no tienen habilidades y el selector no aparece. **Gen 3 y 4** solo tienen principal y secundaria. La oculta existe desde Gen 5.
- **Gen 3 a 5**: la principal y la secundaria dependen de un bit del PID, así que la app busca un PID que conserve género, naturaleza y shiny. La oculta de Gen 5 no toca el PID.
- **Let's Go, Leyendas: Arceus y Leyendas: Z-A** no usan habilidades en combate. El cambio se guarda en el Pokémon, pero no se nota al jugar a esos juegos.
- Si un Pokémon ya tiene una habilidad que no está en su lista, aparece como **(actual)** y se conserva hasta que elijas otra.
- Al cambiar de forma se mantiene el mismo espacio de habilidad. Si la forma nueva no lo tiene, pasa a la principal.
- Algunas habilidades no se pueden conseguir de forma normal en ciertos juegos. El juego las acepta, pero un verificador de legalidad podría señalarlas.

## Objeto equipado y amistad

Elige un Pokémon del equipo o de las cajas y abre **Estadísticas**. El objeto equipado y la amistad están junto a la naturaleza y los IV/EV.

- Busca el objeto escribiendo en el selector. **Solo megapiedras** limita la lista a esas piedras.
- **Equipar** entrega el objeto elegido al Pokémon y **Quitar objeto** lo retira. Para los objetos normales se toma una unidad de la bolsa y se devuelve al retirarlo.
- En Z-A, las megapiedras conservan su unidad en el inventario y se asignan a un solo Pokémon. Para pasar una piedra a otro Pokémon, quítasela primero al que la lleva.
- Si falta un objeto, **Bolsa** abre su ficha en **Dinero y objetos**, donde puedes agregarlo. Regresa después a **Estadísticas** para equiparlo.
- La **Amistad / felicidad** admite valores de 0 a 255. El botón **Máximo (255)** prepara la amistad máxima.

Los cambios de objeto y amistad se aplican con **Guardar partida**. **Deshacer este Pokémon** restaura sus valores anteriores y recalcula las cantidades de la bolsa. Si no hay espacio para devolver un objeto o estás intentando deshacer la adquisición de una piedra que acabas de equipar, la app conserva los cambios y te indica cómo resolverlo.

## Dinero y objetos

La pestaña **Dinero y objetos** permite modificar el dinero de la partida y preparar cambios en la bolsa. El máximo depende del juego (9,999,999 en Z-A, 999,999 en Game Boy y GBA); el botón **Máximo** rellena esa cantidad sin guardarla todavía.

- Busca objetos por nombre o filtra por categoría. **Ver caramelos** muestra Caramelo Raro y los cinco tamaños de Caramelo Exp.
- Selecciona un objeto, indica cuántas unidades quieres y pulsa **+ Agregar** para sumarlas a las que tienes.
- **Fijar cantidad** establece el total exacto. Una cantidad de 0 retira ese objeto de la bolsa.
- La tabla muestra la cantidad preparada y el máximo del objeto. Las megapiedras y otros objetos únicos tienen un máximo de una unidad.
- **Deshacer dinero y objetos** descarta los cambios pendientes de esa pestaña y conserva las ediciones de tus Pokémon.

Pulsa **Guardar partida** para aplicar juntos los cambios de dinero, objetos y Pokémon, con un respaldo automático previo. Si abres otra partida o cierras el editor con cambios pendientes en la bolsa, la app te preguntará antes de descartarlos.

Las filas conservan espacio para las casillas y sus etiquetas. Si reduces la ventana y el contenido no cabe, puedes desplazarte dentro de cada pestaña sin que las filas se compriman.

## Respaldos

Se guardan en `Backups`, dentro de la carpeta de la app. El enlace **Abrir respaldos** abre esa carpeta. Cada guardado incluye:

- `main.original` (o `<nombre>.sav.original`): la partida completa anterior a la edición.
- `cambios.json`: qué Pokémon y valores se cambiaron.
- `destino.anterior`: copia adicional si se sobrescribió un archivo distinto del original abierto.

Para restaurar, cierra el emulador, abre el respaldo `.original` y usa **Guardar como** para guardarlo con su nombre original en la carpeta de guardado del emulador. Puedes abrir `main.original` usando el filtro **Todos los archivos** del selector.

La app impide guardar valores fuera de los límites del juego, movimientos repetidos e incompatibles, y partidas que hayan cambiado fuera de la app desde que se abrieron. También vuelve a abrir y verificar la partida generada antes de reemplazarla.

## Imágenes sin conexión

Incluye sprites e ilustraciones normales y variocolor de las 1,025 especies y de sus formas: 5,368 PNG. Se cargan desde `Assets` y no requieren conexión. El repositorio no las incluye: para tenerlas, descárgalas con `build-tools/Download-Sprites.ps1 -IncludeShiny` y `build-tools/Download-FormSprites.ps1`. Si una forma no tiene imagen propia se muestra la especie base; el Pokémon conserva su forma en la partida. Para volver a descargar lo que falte, usa `build-tools\Download-Sprites.ps1 -IncludeShiny`. Las imágenes de cada forma se descargan aparte con `build-tools\Download-FormSprites.ps1`, que usa la lista de `build-tools\form-sprites.json`, y se guardan como `<especie>-<forma>.png`.

Fuente: [PokeAPI/sprites](https://github.com/PokeAPI/sprites). Los créditos originales están en `Assets/SPRITES-CREDITS.md`. Pokémon y sus imágenes pertenecen a sus respectivos titulares.

## Código y compilación

Código fuente en `src`. Biblioteca de edición: [PKHeX.Core 26.10.18](https://github.com/kwsch/PKHeX/tree/26.10.18), de Kaphotics y colaboradores, bajo GPL-3.0-or-later. La licencia se incluye en `LICENSE`.

Con el SDK de .NET 10 instalado:

```powershell
.\build.ps1
```

Las pruebas de guardado están en `tests`. Se ejecutan sobre una copia aislada; nunca escriben sobre la partida entregada como entrada. Con `--all-games <carpeta>` prueban todos los juegos con partidas generadas, incluidas todas las formas de cada uno, en cada idioma. Con `--strings src` comprueban que todos los textos de la app estén traducidos. Las pruebas visuales pueden ejecutarse con `Editrainer.exe --preview <partida> <captura.png> <carpeta-app> <índice> <pestaña>`.

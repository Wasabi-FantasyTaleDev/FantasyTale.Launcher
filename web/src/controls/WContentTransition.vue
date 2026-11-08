<template>
  <!-- Preserve the panel's layout root; headers and neighboring controls stay still. -->
  <Transition name="content" mode="out-in"><slot /></Transition>
</template>

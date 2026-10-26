// Own read-only diagnosis wrapper; original adapter and linked observation
// objects retain their fixture identity. Register reads have no side effects.
#define main m8_console_main
#define m8_capture m8_adapter_capture
#define m8_bus m8_adapter_bus
#ifdef M8_BSNES
#include "bsnes-console.cpp"
namespace ProbeConsole=SuperFamicom;
#else
#include "ares-console.cpp"
namespace ProbeConsole=ares::SuperFamicom;
#endif
#undef main
#undef m8_capture
#undef m8_bus
static std::string probePrefix;
static std::ofstream probeRows,probePorts,probeWrites;
void m8_dsp_write(unsigned address,unsigned value){probeWrites<<"{\"sampleCount\":"<<nativeAudio.size()/2<<",\"address\":"<<address<<",\"value\":"<<value<<"}\n";}
void m8_capture(int left,int right){
  m8_adapter_capture(left,right);auto index=nativeAudio.size()/2-1;
  if(!((index>=42880&&index<=42910)||(index>=88900&&index<=88930)))return;
  std::ofstream state(probePrefix+".sample-"+std::to_string(index)+".apu.bin",std::ios::binary);
  for(unsigned i=0;i<128;i++){uint8_t value=ProbeConsole::dsp.read(i);state.write((char*)&value,1);}
  for(unsigned i=0;i<65536;i++){uint8_t value=ProbeConsole::dsp.apuram[i];state.write((char*)&value,1);}
  probeRows<<"{\"sample\":"<<index<<",\"masterClock\":"<<masterClock<<",\"left\":"<<left<<",\"right\":"<<right<<"}\n";
}
void m8_bus(unsigned address,unsigned value,bool write,unsigned v,unsigned h){
  m8_adapter_bus(address,value,write,v,h);
  if(write&&(address&0x40fffc)==0x2140)probePorts<<"{\"clock\":"<<masterClock<<",\"fieldIndex\":"<<rawField<<",\"v\":"<<v<<",\"h\":"<<h<<",\"address\":"<<address<<",\"value\":"<<value<<"}\n";
}
int main(int argc,char**argv){
  if(argc<6)return 2;
  probePrefix=argv[5];probeRows.open(probePrefix+".audio-probe.jsonl");probePorts.open(probePrefix+".ports.jsonl");probeWrites.open(probePrefix+".dsp-writes.jsonl");
  return m8_console_main(argc,argv);
}

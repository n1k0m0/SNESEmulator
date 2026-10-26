// Observer declaration injected only into isolated external laboratory builds.
// No clock, bus, register or audio value is modified by the observer.
void m8_capture(int left, int right);
void m8_bus(unsigned address,unsigned value,bool write,unsigned v,unsigned h);
void m8_tick(bool field);
void m8_clock(bool field,unsigned v,unsigned h);
